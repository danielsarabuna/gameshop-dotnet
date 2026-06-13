using Ordering.Application.Abstractions;
using Ordering.Application.Orders.CreateOrder;
using Ordering.Application.Payments;
using Ordering.Application.PromoCodes;
using Ordering.Infrastructure.Integrations;
using Ordering.Infrastructure.Payments;
using Ordering.Infrastructure.Persistence;
using Ordering.API.Consumers;
using Logging;
using EventBus;
using EventBus.RabbitMq;
using Catalog.Grpc;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddWebShopLogging("Ordering");
builder.Services.AddWebShopTracing(builder.Configuration, "Ordering");
builder.Services.AddWebShopMetrics(builder.Configuration, "Ordering");

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

var eventBusProvider = builder.Configuration.GetValue<string>("EventBus:Provider");
if (string.IsNullOrWhiteSpace(eventBusProvider))
{
    eventBusProvider = builder.Environment.IsEnvironment("Docker") ? "RabbitMq" : "Null";
}

if (string.Equals(eventBusProvider, "RabbitMq", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddRabbitMqEventBus(builder.Configuration, bus =>
    {
        bus.AddConsumer<OrderCompletedLogConsumer>();
    });
}
else
{
    builder.Services.AddSingleton<IEventBus>(NullEventBus.Instance);
}

var orderingHealth = builder.Services.AddHealthChecks();
if (usePostgres)
{
    var pgConn = builder.Configuration["Ordering:Postgres:ConnectionString"]
        ?? (builder.Environment.IsEnvironment("Docker")
            ? "Host=postgres;Port=5432;Username=webshop;Password=webshop;Database=ordering"
            : "Host=localhost;Port=5432;Username=webshop;Password=webshop;Database=ordering");
    orderingHealth.AddNpgSql(pgConn, name: "postgres", tags: new[] { "ready" });
}
if (string.Equals(eventBusProvider, "RabbitMq", StringComparison.OrdinalIgnoreCase))
{
    var rabbitHost = builder.Configuration["EventBus:RabbitMq:Host"]
        ?? (builder.Environment.IsEnvironment("Docker") ? "rabbitmq" : "localhost");
    var rabbitUser = builder.Configuration["EventBus:RabbitMq:Username"] ?? "guest";
    var rabbitPass = builder.Configuration["EventBus:RabbitMq:Password"] ?? "guest";
    var rabbitUri = new Uri($"amqp://{rabbitUser}:{rabbitPass}@{rabbitHost}:5672/");
    orderingHealth.AddRabbitMQ(
        async _ =>
        {
            var factory = new RabbitMQ.Client.ConnectionFactory { Uri = rabbitUri };
            return await factory.CreateConnectionAsync();
        },
        name: "rabbitmq",
        tags: new[] { "ready" });
}

var catalogTransport = builder.Configuration.GetValue<string>("Catalog:Transport");
if (string.IsNullOrWhiteSpace(catalogTransport))
{
    catalogTransport = builder.Environment.IsEnvironment("Docker") ? "Grpc" : "Http";
}

if (string.Equals(catalogTransport, "Grpc", StringComparison.OrdinalIgnoreCase))
{
    var grpcUrl = builder.Configuration["Catalog:GrpcUrl"]
        ?? (builder.Environment.IsEnvironment("Docker") ? "http://catalog-api:8080" : "http://localhost:5101");

    // Allow HTTP/2 cleartext (h2c) without TLS for in-cluster gRPC.
    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

    builder.Services
        .AddGrpcClient<CatalogInternal.CatalogInternalClient>(options =>
        {
            options.Address = new Uri(grpcUrl);
        });
    builder.Services.AddScoped<ICatalogClient, GrpcCatalogClient>();
}
else
{
    builder.Services.AddHttpClient<ICatalogClient, HttpCatalogClient>(client =>
    {
        var baseUrl = builder.Configuration["Catalog:BaseUrl"] ?? "http://localhost:5101";
        client.BaseAddress = new Uri(baseUrl);
    });
}

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

app.UseWebShopRequestLogging();
app.UseCors();

app.MapWebShopHealth();
app.MapWebShopMetrics();

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

namespace Ordering.API
{
    public sealed class Program;
}
