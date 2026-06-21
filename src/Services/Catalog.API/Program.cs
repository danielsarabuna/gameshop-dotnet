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

// Регистрация HTTP-клиентов и сервисов
builder.Services.AddHttpClient<ICatalogSyncService, CatalogSyncService>();
builder.Services.AddHttpClient<ISupabasePlayerVerifier, SupabasePlayerVerifier>();
builder.Services.AddHostedService<CatalogSyncBackgroundService>();

var catalogHealth = builder.Services.AddHealthChecks();
if (string.Equals(builder.Configuration["Catalog:Storage"], "Mongo", StringComparison.OrdinalIgnoreCase)
    || builder.Environment.IsEnvironment("Docker"))
{
    var mongoConn = builder.Configuration["Catalog:Mongo:ConnectionString"]
        ?? (builder.Environment.IsEnvironment("Docker") ? "mongodb://mongodb:27017" : "mongodb://localhost:27017");
    catalogHealth.AddMongoDb(_ => new MongoDB.Driver.MongoClient(mongoConn), name: "mongo", tags: new[] { "ready" });
}
builder.WebHost.ConfigureKestrel(options =>
{
    var httpPort = builder.Configuration.GetValue<int?>("Kestrel:HttpPort") ?? 8080;
    var grpcPort = builder.Configuration.GetValue<int?>("Kestrel:GrpcPort") ?? 8081;
    options.ListenAnyIP(httpPort, listen => listen.Protocols = HttpProtocols.Http1);
    options.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
});
var storageMode = builder.Configuration.GetValue<string>("Catalog:Storage");
if (string.IsNullOrWhiteSpace(storageMode))
{
    storageMode = builder.Environment.IsEnvironment("Docker") ? "Mongo" : "InMemory";
}

if (string.Equals(storageMode, "Mongo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ICatalogStore, MongoCatalogStore>();
}
else
{
    builder.Services.AddSingleton<ICatalogStore, InMemoryCatalogStore>();
}
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddGrpc();
builder.Services.AddCustomExceptionHandler();

var app = builder.Build();

app.UseExceptionHandler();
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

// Эндпоинт получения товаров с поддержкой регионов, сторов и версий игры
app.MapGet("/api/v1/catalog/items", (string? region, string? store, string? gameVersion, ICatalogStore catalogStore) =>
{
    return Results.Ok(catalogStore.GetFiltered(region, store, gameVersion));
});

// Эндпоинт получения доступных провайдеров оплаты под регион и стор
app.MapGet("/api/v1/catalog/payment-providers", (string? region, string? store, string? gameVersion, ICatalogStore catalogStore) =>
{
    return Results.Ok(catalogStore.GetPaymentProviders(region, store, gameVersion));
});

app.MapGet("/api/v1/catalog/items/{id:guid}", (Guid id, ICatalogStore catalogStore) =>
{
    var item = catalogStore.GetById(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

// Эндпоинт ручного форсированного обновления конфигов из Supabase
app.MapPost("/api/v1/catalog/reload", async (ICatalogSyncService syncService, CancellationToken ct) =>
{
    await syncService.SyncAllAsync(ct);
    return Results.Ok(new { message = "Catalog sync completed." });
});

app.Run();

namespace Catalog.API
{
    public sealed class Program;
}
