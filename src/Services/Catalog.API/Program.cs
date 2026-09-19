using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Auth;
using BuildingBlocks.Exceptions;
using Catalog.API.Configuration;
using Catalog.API.Grpc;
using Catalog.API.Services;
using Catalog.API.Storage;
using Logging;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

if (!builder.Environment.IsDevelopment()
    && string.IsNullOrWhiteSpace(builder.Configuration["GameTicketJwt:PrivateKeyPemBase64"]))
{
    throw new InvalidOperationException("GameTicketJwt:PrivateKeyPemBase64 is required outside Development.");
}

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
builder.Services.AddHttpClient<CatalogProvider>();
builder.Services.AddSingleton<ICatalogProvider>(services => services.GetRequiredService<CatalogProvider>());
builder.Services.AddHttpClient<ISupabasePlayerVerifier, SupabasePlayerVerifier>();
builder.Services.AddSingleton<CatalogCacheInvalidationAuthenticator>();
builder.Services.AddGameTicketTokenIssuer(builder.Configuration);

builder.Services.AddHealthChecks();
builder.WebHost.ConfigureKestrel(options =>
{
    var httpPort = builder.Configuration.GetValue<int?>("Kestrel:HttpPort") ?? 8080;
    var grpcPort = builder.Configuration.GetValue<int?>("Kestrel:GrpcPort") ?? 8081;
    options.ListenAnyIP(httpPort, listen => listen.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
});

// Catalog cache is in-memory only; configs are fetched lazily from Supabase on first access.
builder.Services.AddSingleton<ICatalogStore, InMemoryCatalogStore>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        policy
            .WithOrigins(configuredOrigins.Length > 0
                ? configuredOrigins
                : ["http://localhost:5173", "http://localhost:5200", "https://localhost:5200"])
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddGrpc();
builder.Services.AddOpenApi();
builder.Services.AddCustomExceptionHandler();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("player-resolution", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.UseCors();
app.UseRateLimiter();
app.UseWebShopSecurityHeaders();
app.UseWebShopRequestLogging();

app.MapWebShopHealth();
app.MapWebShopMetrics();
app.MapGrpcService<CatalogInternalGrpcService>();

// Эндпоинты аутентификации игрока (диплинк через тикет vs прямой ввод ID)
app.MapPost("/api/v1/auth/claim-ticket", async (ClaimTicketRequest request, ISupabasePlayerVerifier verifier, GameTicketTokenIssuer issuer, CancellationToken ct) =>
{
    var result = await verifier.ClaimTicketAsync(request.Ticket, ct);
    if (!result.IsValid)
    {
        return Results.BadRequest(result);
    }

    var token = issuer.Issue(result.UserId, result.Region, result.Store, result.GameVersion, deliveryContractVersion: result.DeliveryContractVersion);
    return Results.Ok(new PlayerSessionResult(result, token.AccessToken, token.ExpiresAtUtc, "game"));
});

app.MapPost("/api/v1/auth/resolve-player", async (
    ResolvePlayerRequest request,
    ISupabasePlayerVerifier verifier,
    GameTicketTokenIssuer issuer,
    CancellationToken ct) =>
{
    var result = await verifier.ResolvePlayerAsync(request.PlayerId, ct);
    if (!result.IsValid)
    {
        return Results.BadRequest(result);
    }

    var token = issuer.Issue(result.UserId, result.Region, result.Store, result.GameVersion, "recipient", result.DeliveryContractVersion);
    return Results.Ok(new PlayerSessionResult(result, token.AccessToken, token.ExpiresAtUtc, "recipient"));
}).RequireRateLimiting("player-resolution");

// Эндпоинт получения товаров с поддержкой регионов, сторов и версий игры (lazy fetch from Supabase)
app.MapGet("/api/v1/catalog/items", async (string? region, string? store, string? gameVersion, string? locale, ICatalogProvider provider, SupabaseOptions options, CancellationToken ct) =>
{
    // No context params → Global config (anonymous visitor, not from the game deeplink).
    var config = await provider.GetOrFetchAsync(
        string.IsNullOrWhiteSpace(region) ? options.DefaultRegion : region,
        string.IsNullOrWhiteSpace(store) ? options.DefaultStore : store,
        string.IsNullOrWhiteSpace(gameVersion) ? options.DefaultGameVersion : gameVersion,
        ct);
    return Results.Ok(config?.Items.Select(item => item.Localize(locale)) ?? []);
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

app.MapGet("/api/v1/catalog/items/{id:guid}", async (Guid id, string? region, string? store, string? gameVersion, string? locale, ICatalogProvider provider, SupabaseOptions options, CancellationToken ct) =>
{
    var catalog = await provider.GetOrFetchAsync(
        string.IsNullOrWhiteSpace(region) ? options.DefaultRegion : region,
        string.IsNullOrWhiteSpace(store) ? options.DefaultStore : store,
        string.IsNullOrWhiteSpace(gameVersion) ? options.DefaultGameVersion : gameVersion,
        ct);
    var item = catalog?.Items.FirstOrDefault(candidate => candidate.Id == id)?.Localize(locale);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

// Стриминг закэшированных ассетов (картинки алмазов/подписок, скачанные с Supabase)
app.MapGet("/api/v1/catalog/assets/{region}/{store}/{version}/{*file}", async (string region, string store, string version, string file, HttpResponse response, ICatalogProvider provider, CancellationToken ct) =>
{
    var asset = await provider.OpenAssetAsync(region, store, version, file, ct);
    if (asset is null)
    {
        return Results.NotFound();
    }

    // Path is versioned → safe to cache aggressively on the client/CDN.
    response.Headers.CacheControl = "public, max-age=86400, immutable";
    return Results.Stream(asset.Content, contentType: GuessContentType(file), lastModified: asset.LastModified);
});

app.MapPost("/api/v1/catalog/cache-invalidation", async (
    HttpRequest request,
    CatalogCacheInvalidationAuthenticator authenticator,
    ICatalogProvider provider,
    CancellationToken ct) =>
{
    if (!authenticator.IsConfigured)
    {
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Catalog invalidation is not configured.");
    }

    if (request.ContentLength is > 4096)
    {
        return Results.BadRequest(new { error = "Payload is too large." });
    }

    using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
    var body = await reader.ReadToEndAsync(ct);
    if (!authenticator.TryValidate(
            request.Headers[CatalogCacheInvalidationAuthenticator.TimestampHeader],
            request.Headers[CatalogCacheInvalidationAuthenticator.SignatureHeader],
            body,
            DateTimeOffset.UtcNow,
            out var isReplay))
    {
        return Results.Unauthorized();
    }

    if (isReplay)
    {
        return Results.Ok(new { message = "Catalog cache invalidation already processed." });
    }

    CatalogCacheInvalidationRequest? payload;
    try
    {
        payload = JsonSerializer.Deserialize<CatalogCacheInvalidationRequest>(body);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Payload must be valid JSON." });
    }

    if (!IsCatalogKeyPart(payload?.Region) || !IsCatalogKeyPart(payload?.Store) || !IsCatalogKeyPart(payload?.GameVersion))
    {
        return Results.BadRequest(new { error = "region, store, and gameVersion must contain only letters, digits, dots, underscores, or hyphens." });
    }

    var invalidated = await provider.InvalidateAsync(payload!.Region.Trim(), payload.Store.Trim(), payload.GameVersion.Trim(), ct);
    return Results.Ok(new { message = "Catalog cache invalidated.", invalidated });
}).AllowAnonymous();

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

static bool IsCatalogKeyPart(string? value) => value is { Length: > 0 and <= 80 }
    && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

app.Run();

namespace Catalog.API
{
    public sealed class Program;
}

public sealed record PlayerSessionResult(PlayerContextResult Player, string AccessToken, DateTimeOffset ExpiresAtUtc, string SessionKind)
{
    public bool IsValid => Player.IsValid;
    public string UserId => Player.UserId;
    public string Region => Player.Region;
    public string Store => Player.Store;
    public string GameVersion => Player.GameVersion;
    public string? PlayerName => Player.PlayerName;
    public string? ErrorMessage => Player.ErrorMessage;
    public string? ErrorCode => Player.ErrorCode;
    public int DeliveryContractVersion => Player.DeliveryContractVersion;
}

public sealed record ClaimTicketRequest(Guid Ticket);
public sealed record ResolvePlayerRequest(string PlayerId);
public sealed record CatalogCacheInvalidationRequest(
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("store")] string Store,
    [property: JsonPropertyName("gameVersion")] string GameVersion);
