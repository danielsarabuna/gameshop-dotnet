using BuildingBlocks.Exceptions;
using Catalog.API.Configuration;
using Catalog.API.Grpc;
using Catalog.API.Services;
using Catalog.API.Storage;
using Logging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Catalog");
builder.Services.AddWebShopTracing(builder.Configuration, "Catalog");
builder.Services.AddWebShopMetrics(builder.Configuration, "Catalog");

// Регистрация RemoteCatalogOptions и SupabaseOptions
var remoteOptions = new RemoteCatalogOptions();
builder.Configuration.GetSection(RemoteCatalogOptions.SectionName).Bind(remoteOptions);
builder.Services.AddSingleton(remoteOptions);

var supabaseOptions = new SupabaseOptions();
builder.Configuration.GetSection(SupabaseOptions.SectionName).Bind(supabaseOptions);
builder.Services.AddSingleton(supabaseOptions);

// Регистрация HTTP-клиентов и сервисов (lazy catalog provider + Supabase player verifier)
builder.Services.AddHttpClient<ICatalogProvider, CatalogProvider>();
builder.Services.AddHttpClient<ISupabasePlayerVerifier, SupabasePlayerVerifier>();
builder.Services.AddHostedService<CatalogPreloaderHostedService>();

builder.Services.AddHealthChecks();
builder.WebHost.ConfigureKestrel(options =>
{
    var httpPort = builder.Configuration.GetValue<int?>("Kestrel:HttpPort") ?? 8080;
    var grpcPort = builder.Configuration.GetValue<int?>("Kestrel:GrpcPort") ?? 8081;
    options.ListenAnyIP(httpPort, listen => listen.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
});

// Catalog cache is in-memory only; configs are fetched lazily from Supabase per request.
builder.Services.AddSingleton<ICatalogStore, InMemoryCatalogStore>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddGrpc();
builder.Services.AddCustomExceptionHandler();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseWebShopSecurityHeaders();
app.UseWebShopRequestLogging();

app.MapWebShopHealth();
app.MapWebShopMetrics();
app.MapGrpcService<CatalogInternalGrpcService>();

// Эндпоинты аутентификации игрока (диплинк через тикет vs прямой ввод ID)
app.MapPost("/api/v1/auth/claim-ticket", async (Guid ticket, ISupabasePlayerVerifier verifier, CancellationToken ct) =>
{
    var result = await verifier.ClaimTicketAsync(ticket, ct);
    return result.IsValid ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/v1/auth/verify-player", async (string userId, ISupabasePlayerVerifier verifier, CancellationToken ct) =>
{
    var result = await verifier.VerifyDirectPlayerIdAsync(userId, ct);
    return result.IsValid ? Results.Ok(result) : Results.BadRequest(result);
});

// Эндпоинт получения товаров с поддержкой регионов, сторов и версий игры (lazy fetch from Supabase)
app.MapGet("/api/v1/catalog/items", async (string? region, string? store, string? gameVersion, ICatalogProvider provider, SupabaseOptions options, CancellationToken ct) =>
{
    // No context params → Global config (anonymous visitor, not from the game deeplink).
    var config = await provider.GetOrFetchAsync(
        string.IsNullOrWhiteSpace(region) ? options.DefaultRegion : region,
        string.IsNullOrWhiteSpace(store) ? options.DefaultStore : store,
        string.IsNullOrWhiteSpace(gameVersion) ? options.DefaultGameVersion : gameVersion,
        ct);
    return Results.Ok(config?.Items ?? new List<Catalog.API.Storage.CatalogItem>());
});

// Эндпоинт получения доступных провайдеров оплаты под регион и стор
app.MapGet("/api/v1/catalog/payment-providers", async (string? region, string? store, string? gameVersion, ICatalogProvider provider, SupabaseOptions options, CancellationToken ct) =>
{
    var config = await provider.GetOrFetchAsync(
        string.IsNullOrWhiteSpace(region) ? options.DefaultRegion : region,
        string.IsNullOrWhiteSpace(store) ? options.DefaultStore : store,
        string.IsNullOrWhiteSpace(gameVersion) ? options.DefaultGameVersion : gameVersion,
        ct);
    return Results.Ok(new PaymentProviderConfig(config?.PaymentProviders ?? new List<PaymentProviderDto>()));
});

app.MapGet("/api/v1/catalog/items/{id:guid}", async (Guid id, ICatalogStore catalogStore, ICatalogProvider provider, CancellationToken ct) =>
{
    var item = catalogStore.GetItemById(id);
    if (item is null)
    {
        await provider.GetOrFetchAsync("global", "global", "global", ct);
        item = catalogStore.GetItemById(id);
    }
    return item is null ? Results.NotFound() : Results.Ok(item);
});

// Стриминг закэшированных ассетов (картинки алмазов/подписок, скачанные с Supabase)
app.MapGet("/api/v1/catalog/assets/{region}/{store}/{version}/{*file}", async (string region, string store, string version, string file, HttpResponse response, RemoteCatalogOptions options, ICatalogProvider provider, CancellationToken ct) =>
{
    var root = Path.GetFullPath(options.AssetCacheDir);
    if (!root.EndsWith(Path.DirectorySeparatorChar))
    {
        root += Path.DirectorySeparatorChar;
    }
    var path = Path.GetFullPath(Path.Combine(root, region, store, version, file));
    if (!path.StartsWith(root, StringComparison.Ordinal))
    {
        return Results.NotFound();
    }

    if (!File.Exists(path))
    {
        var downloaded = await provider.EnsureAssetDownloadedAsync(region, store, version, file, ct);
        if (!downloaded || !File.Exists(path))
        {
            return Results.NotFound();
        }
    }

    // Path is versioned → safe to cache aggressively on the client/CDN.
    response.Headers.CacheControl = "public, max-age=86400, immutable";
    return Results.File(path, contentType: GuessContentType(file),
        lastModified: File.GetLastWriteTimeUtc(path));
});

// Эндпоинт ручного форсированного обновления конфига для конкретной версии
app.MapPost("/api/v1/catalog/reload", async (string? region, string? store, string? gameVersion, ICatalogProvider provider, CancellationToken ct) =>
{
    var config = await provider.ForceRefreshAsync(region ?? "russia", store ?? "ru_store", gameVersion ?? "0.0.36", ct);
    return Results.Ok(new { message = "Catalog reloaded.", items = config?.Items.Count ?? 0 });
});

static string GuessContentType(string fileName)
    => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream"
    };

app.Run();

namespace Catalog.API
{
    public sealed class Program;
}
