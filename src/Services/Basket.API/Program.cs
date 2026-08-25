using System.Globalization;
using System.Security.Claims;
using Basket.API.Storage;
using BuildingBlocks.Exceptions;
using Catalog.Grpc;
using EventBus;
using EventBus.RabbitMq;
using Grpc.Core;
using IntegrationEvents;
using BuildingBlocks.Auth;
using Logging;
using ShoppingBasket = Basket.API.Storage.Basket;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Basket");
builder.Services.AddWebShopTracing(builder.Configuration, "Basket");
builder.Services.AddWebShopMetrics(builder.Configuration, "Basket");
builder.Services.AddCustomExceptionHandler();
builder.Services.AddWebShopJwtAuthentication(builder.Configuration);

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

var eventBusProvider = builder.Configuration.GetValue<string>("EventBus:Provider");
if (string.IsNullOrWhiteSpace(eventBusProvider))
{
    eventBusProvider = builder.Environment.IsEnvironment("Docker") ? "RabbitMq" : "Null";
}

if (string.Equals(eventBusProvider, "RabbitMq", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddRabbitMqEventBus(builder.Configuration);
}
else
{
    builder.Services.AddSingleton<IEventBus>(NullEventBus.Instance);
}

var grpcUrl = builder.Configuration["Catalog:GrpcUrl"]
    ?? (builder.Environment.IsEnvironment("Docker") ? "http://catalog-api:8081" : "http://localhost:8081");

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

builder.Services.AddGrpcClient<CatalogInternal.CatalogInternalClient>(options =>
{
    options.Address = new Uri(grpcUrl);
});

builder.Services.AddSingleton<IBasketCatalogClient, GrpcBasketCatalogClient>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseWebShopSecurityHeaders();
app.UseWebShopRequestLogging();
app.UseWebShopAuth();

app.MapWebShopHealth();
app.MapWebShopMetrics();

app.MapGet("/api/v1/basket/{userId}", (string userId, HttpContext httpContext, IBasketStore store) =>
{
    ValidateUserAuthorization(httpContext, userId);

    var basket = store.Get(userId);
    return Results.Ok(basket ?? new ShoppingBasket(userId, "USD", []));
});

app.MapPut("/api/v1/basket/{userId}", async (
    string userId,
    UpdateBasketRequest request,
    HttpContext httpContext,
    IBasketStore store,
    IBasketCatalogClient catalog,
    CancellationToken cancellationToken) =>
{
    ValidateUserAuthorization(httpContext, userId);

    if (request.Items is null || request.Items.Count == 0)
    {
        return Results.BadRequest(new { error = "Basket must contain at least one item." });
    }

    var validatedItems = new List<BasketItem>();
    string? basketCurrency = null;

    foreach (var item in request.Items)
    {
        if (item.Quantity <= 0)
        {
            return Results.BadRequest(new { error = $"Quantity for product '{item.Title}' must be positive." });
        }

        BasketProduct? product;
        try
        {
            product = await catalog.GetProductAsync(item.ProductId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-closed: without the catalog we cannot verify price/availability.
            // Trusting client-supplied prices here would allow tampering during outages.
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (product is null || !product.IsActive)
        {
            return Results.BadRequest(new { error = $"Product '{item.Title}' is no longer active or available." });
        }

        // Overwrite UnitPrice with actual price from Catalog to prevent price tampering.
        validatedItems.Add(new BasketItem(
            product.Id,
            product.Title,
            product.Price,
            item.Quantity));

        // Single-currency baskets only — mixed currencies cannot check out coherently.
        if (basketCurrency is null)
        {
            basketCurrency = product.Currency;
        }
        else if (!string.Equals(basketCurrency, product.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "All products in the basket must use the same currency." });
        }
    }

    var basket = new ShoppingBasket(userId, basketCurrency ?? "USD", validatedItems);
    store.Upsert(basket);
    return Results.Ok(basket);
});

app.MapDelete("/api/v1/basket/{userId}", (string userId, HttpContext httpContext, IBasketStore store) =>
{
    ValidateUserAuthorization(httpContext, userId);

    store.Delete(userId);
    return Results.NoContent();
});

app.MapPost("/api/v1/basket/checkout", async (
    CheckoutBasketRequest request,
    HttpContext httpContext,
    IBasketStore store,
    IEventBus eventBus,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.UserId))
    {
        return Results.BadRequest(new { error = "UserId is required." });
    }

    ValidateUserAuthorization(httpContext, request.UserId);

    var basket = store.Get(request.UserId);
    if (basket is null || basket.Items.Count == 0)
    {
        return Results.BadRequest(new { error = "Basket is empty or not found." });
    }

    var checkoutEvent = new BasketCheckout(
        Id: Guid.NewGuid(),
        OccurredAtUtc: DateTimeOffset.UtcNow,
        UserId: request.UserId,
        Items: basket.Items.Select(i => new BasketCheckoutItem(i.ProductId, i.Title, i.UnitPrice, i.Quantity)).ToList(),
        Total: basket.Items.Sum(i => i.UnitPrice * i.Quantity),
        Currency: string.IsNullOrWhiteSpace(basket.Currency) ? "USD" : basket.Currency
    );

    await eventBus.PublishAsync(checkoutEvent, cancellationToken);
    store.Delete(request.UserId);

    return Results.Accepted(value: new { message = "Checkout initiated.", checkoutId = checkoutEvent.Id });
});

app.Run();

static void ValidateUserAuthorization(HttpContext context, string targetUserId)
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var authUserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        var isUserAdmin = context.User.IsInRole("Admin");

        if (!string.IsNullOrWhiteSpace(authUserId)
            && !isUserAdmin
            && !string.Equals(authUserId, targetUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access denied: You can only manage your own basket.");
        }
    }
}

public record CheckoutBasketRequest(string UserId);

namespace Basket.API
{
    public sealed class Program;
}
