using Ordering.Application.Abstractions;
using Ordering.Application.Orders.CreateOrder;
using Ordering.Application.Payments;
using Ordering.Application.PromoCodes;
using Ordering.Infrastructure.Integrations;
using Ordering.Infrastructure.Payments;
using Ordering.Infrastructure.Persistence;
using Logging;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging();
var storageMode = builder.Configuration.GetValue<string>("Ordering:Storage");
if (string.IsNullOrWhiteSpace(storageMode))
{
    storageMode = builder.Environment.IsEnvironment("Docker") ? "Postgres" : "InMemory";
}

var usePostgres = string.Equals(storageMode, "Postgres", StringComparison.OrdinalIgnoreCase);
if (usePostgres)
{
    builder.Services.AddSingleton<IOrderRepository, PostgresOrderRepository>();
    builder.Services.AddSingleton<IPaymentStore, PostgresPaymentStore>();
    builder.Services.AddSingleton<IWebhookIdempotencyStore, PostgresWebhookIdempotencyStore>();
    builder.Services.AddSingleton<OrderingDatabaseInitializer>();
}
else
{
    builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
    builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
    builder.Services.AddSingleton<IWebhookIdempotencyStore, InMemoryWebhookIdempotencyStore>();
}

builder.Services.AddSingleton<IPromoCodeStore, InMemoryPromoCodeStore>();
builder.Services.AddSingleton<IPaymentProviderAccessor, PaymentProviderAccessor>();

builder.Services.AddHttpClient<ICatalogClient, HttpCatalogClient>(client =>
{
    var baseUrl = builder.Configuration["Catalog:BaseUrl"] ?? "http://localhost:5101";
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHttpClient<IPurchaseRecorder, SupabasePurchaseRecorder>();

builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<ApplyPromoCodeHandler>();
builder.Services.AddScoped<CreatePaymentHandler>();
builder.Services.AddScoped<HandleWebhookHandler>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins("http://localhost:5200", "https://localhost:5200")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

if (usePostgres)
{
    var initializer = app.Services.GetRequiredService<OrderingDatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/v1/orders", async (CreateOrderRequest request, CreateOrderHandler handler, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/orders/{result.OrderId}", result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/v1/orders/create", async (CreateOrderRequest request, CreateOrderHandler handler, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/orders/{result.OrderId}", result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/orders/create", async (CreateOrderRequest request, CreateOrderHandler handler, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return Results.Created($"/orders/{result.OrderId}", result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/v1/orders/{id:guid}", async (Guid id, IOrderRepository repository, CancellationToken cancellationToken) =>
{
    var order = await repository.GetAsync(id, cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapGet("/orders/{id:guid}", async (Guid id, IOrderRepository repository, CancellationToken cancellationToken) =>
{
    var order = await repository.GetAsync(id, cancellationToken);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapPost("/api/v1/promocodes/apply", async (ApplyPromoCodeRequest request, ApplyPromoCodeHandler handler, CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(request, cancellationToken);
    return Results.Ok(result);
});

app.MapPost("/promocodes/apply", async (ApplyPromoCodeRequest request, ApplyPromoCodeHandler handler, CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(request, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/api/v1/payment-methods", () =>
{
    var methods = PaymentMethodsData.GetAvailableMethods();
    return Results.Ok(methods);
});

app.MapPost("/api/v1/payments/{provider}", async (
    string provider,
    CreatePaymentRequest request,
    CreatePaymentHandler handler,
    CancellationToken cancellationToken) =>
{
    if (!TryParseProvider(provider, out var method))
    {
        return Results.BadRequest(new { error = "Unknown provider." });
    }

    try
    {
        var result = await handler.HandleAsync(request.OrderId, method, cancellationToken);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/payments/{provider}", async (
    string provider,
    CreatePaymentRequest request,
    CreatePaymentHandler handler,
    CancellationToken cancellationToken) =>
{
    if (!TryParseProvider(provider, out var method))
    {
        return Results.BadRequest(new { error = "Unknown provider." });
    }

    try
    {
        var result = await handler.HandleAsync(request.OrderId, method, cancellationToken);
        return Results.Ok(result);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/v1/webhooks/{provider}", async (
    HttpRequest httpRequest,
    string provider,
    HandleWebhookHandler handler,
    IPaymentProviderAccessor accessor,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryParseProvider(provider, out var method))
    {
        return Results.BadRequest(new { error = "Unknown provider." });
    }

    var secret = configuration[$"Webhooks:{provider}:Secret"] ?? configuration["Webhooks:Secret"];
    var signature = httpRequest.Headers["X-Webhook-Secret"].ToString();

    if (!string.IsNullOrWhiteSpace(secret) && !string.Equals(signature, secret, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    using var reader = new StreamReader(httpRequest.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);
    var bodyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));

    var providerInstance = accessor.GetProvider(method);
    WebhookResult? webhookResult = null;

    if (providerInstance is not null)
    {
        try
        {
            webhookResult = await providerInstance.ParseWebhookAsync(bodyStream, signature, cancellationToken);
        }
        catch (NotImplementedException)
        {
        }
    }

    if (webhookResult is null)
    {
        return Results.BadRequest(new { error = "Webhook parsing not implemented." });
    }

    var request = new PaymentWebhookRequest(
        webhookResult.EventId,
        webhookResult.OrderId,
        webhookResult.PaymentId,
        webhookResult.Status
    );

    try
    {
        var processed = await handler.HandleAsync(method, request, cancellationToken);
        return Results.Ok(new { processed });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/webhooks/{provider}", async (
    HttpRequest httpRequest,
    string provider,
    HandleWebhookHandler handler,
    IPaymentProviderAccessor accessor,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    if (!TryParseProvider(provider, out var method))
    {
        return Results.BadRequest(new { error = "Unknown provider." });
    }

    var secret = configuration[$"Webhooks:{provider}:Secret"] ?? configuration["Webhooks:Secret"];
    var signature = httpRequest.Headers["X-Webhook-Secret"].ToString();

    if (!string.IsNullOrWhiteSpace(secret) && !string.Equals(signature, secret, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    using var reader = new StreamReader(httpRequest.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);
    var bodyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));

    var providerInstance = accessor.GetProvider(method);
    WebhookResult? webhookResult = null;

    if (providerInstance is not null)
    {
        try
        {
            webhookResult = await providerInstance.ParseWebhookAsync(bodyStream, signature, cancellationToken);
        }
        catch (NotImplementedException)
        {
        }
    }

    if (webhookResult is null)
    {
        return Results.BadRequest(new { error = "Webhook parsing not implemented." });
    }

    var request = new PaymentWebhookRequest(
        webhookResult.EventId,
        webhookResult.OrderId,
        webhookResult.PaymentId,
        webhookResult.Status
    );

    try
    {
        var processed = await handler.HandleAsync(method, request, cancellationToken);
        return Results.Ok(new { processed });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

await app.RunAsync();

static bool TryParseProvider(string provider, out Ordering.Domain.Payments.PaymentMethod method)
{
    method = default;
    if (string.IsNullOrWhiteSpace(provider))
    {
        return false;
    }

    switch (provider.Trim().ToLowerInvariant())
    {
        case "stripe":
            method = Ordering.Domain.Payments.PaymentMethod.Stripe;
            return true;
        case "paypal":
            method = Ordering.Domain.Payments.PaymentMethod.PayPal;
            return true;
        case "yookassa":
        case "yoo_kassa":
        case "yoo-kassa":
            method = Ordering.Domain.Payments.PaymentMethod.YooKassa;
            return true;
        case "corvuspay":
        case "corvus_pay":
            method = Ordering.Domain.Payments.PaymentMethod.CorvusPay;
            return true;
        case "xsolla":
            method = Ordering.Domain.Payments.PaymentMethod.Xsolla;
            return true;
        default:
            return false;
    }
}
