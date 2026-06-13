using Catalog.API.Grpc;
using Catalog.API.Storage;
using Logging;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Catalog");
builder.Services.AddWebShopTracing(builder.Configuration, "Catalog");
builder.Services.AddWebShopMetrics(builder.Configuration, "Catalog");

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

var app = builder.Build();

app.UseWebShopRequestLogging();

app.MapWebShopHealth();
app.MapWebShopMetrics();
app.MapGrpcService<CatalogInternalGrpcService>();

app.MapGet("/api/v1/catalog/items", (ICatalogStore store) =>
{
    return Results.Ok(store.GetAll());
});

app.MapGet("/api/v1/catalog/items/{id:guid}", (Guid id, ICatalogStore store) =>
{
    var item = store.GetById(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.Run();

namespace Catalog.API
{
    public sealed class Program;
}
