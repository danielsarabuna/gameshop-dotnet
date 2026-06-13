using Basket.API.Storage;
using Logging;
using ShoppingBasket = Basket.API.Storage.Basket;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Basket");
builder.Services.AddWebShopTracing(builder.Configuration, "Basket");
builder.Services.AddWebShopMetrics(builder.Configuration, "Basket");

var basketHealth = builder.Services.AddHealthChecks();
if (string.Equals(builder.Configuration["Basket:Storage"], "Redis", StringComparison.OrdinalIgnoreCase)
    || builder.Environment.IsEnvironment("Docker"))
{
    var redisConn = builder.Configuration["Basket:Redis:ConnectionString"]
        ?? (builder.Environment.IsEnvironment("Docker") ? "redis:6379" : "localhost:6379");
    basketHealth.AddRedis(redisConn, name: "redis", tags: new[] { "ready" });
}
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

app.UseWebShopRequestLogging();

app.MapWebShopHealth();
app.MapWebShopMetrics();

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

namespace Basket.API
{
    public sealed class Program;
}
