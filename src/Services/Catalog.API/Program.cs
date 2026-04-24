using Catalog.API.Storage;
using Logging;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging();
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

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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
