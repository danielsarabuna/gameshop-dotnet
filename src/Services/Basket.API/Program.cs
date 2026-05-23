using Basket.API.Storage;
using Logging;
using ShoppingBasket = Basket.API.Storage.Basket;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging();
var storageMode = builder.Configuration.GetValue<string>("Basket:Storage");
if (string.IsNullOrWhiteSpace(storageMode))
{
    storageMode = builder.Environment.IsEnvironment("Docker") ? "Redis" : "InMemory";
}

if (string.Equals(storageMode, "Redis", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IBasketStore, RedisBasketStore>();
}
else
{
    builder.Services.AddSingleton<IBasketStore, InMemoryBasketStore>();
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/v1/basket/{userId}", (string userId, IBasketStore store) =>
{
    var basket = store.Get(userId) ?? new ShoppingBasket(userId, []);
    return Results.Ok(basket);
});

app.MapPut("/api/v1/basket/{userId}", (string userId, UpdateBasketRequest request, IBasketStore store) =>
{
    var basket = new ShoppingBasket(userId, request.Items);
    store.Upsert(basket);
    return Results.Ok(basket);
});

app.MapDelete("/api/v1/basket/{userId}", (string userId, IBasketStore store) =>
{
    store.Delete(userId);
    return Results.NoContent();
});

app.Run();
