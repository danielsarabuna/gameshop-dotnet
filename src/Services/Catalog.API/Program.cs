using Catalog.API.Storage;
using Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging();
builder.Services.AddSingleton<ICatalogStore, InMemoryCatalogStore>();

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
