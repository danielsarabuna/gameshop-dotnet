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
using System.Text.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using BuildingBlocks.Exceptions;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;

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
    builder.Services.AddSingleton<IPromoCodeStore, PostgresPromoCodeStore>();
    builder.Services.AddSingleton<OrderingDatabaseInitializer>();
}
else
{
    builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
    builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
    builder.Services.AddSingleton<IWebhookIdempotencyStore, InMemoryWebhookIdempotencyStore>();
    builder.Services.AddSingleton<IPromoCodeStore, InMemoryPromoCodeStore>();
}
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
        bus.AddConsumer<BasketCheckoutConsumer>();
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

builder.Services.AddHttpClient<ISupabaseOrderDelivery, SupabaseOrderDeliveryService>();

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

builder.Services.AddCustomExceptionHandler();

// Outbox: deferred side effects (event publication + Supabase delivery) drained with retries.
builder.Services.AddHostedService<Ordering.API.Infrastructure.OutboxDispatcherHostedService>();

var app = builder.Build();

if (usePostgres)
{
    var initializer = app.Services.GetRequiredService<OrderingDatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.UseExceptionHandler();
app.UseWebShopSecurityHeaders();
app.UseWebShopRequestLogging();
app.UseCors();

app.MapWebShopHealth();
app.MapWebShopMetrics();

app.MapPost("/api/v1/orders", CreateOrder);
app.MapPost("/api/v1/orders/create", CreateOrder);
app.MapPost("/orders/create", CreateOrder);

app.MapGet("/api/v1/orders/{id:guid}", GetOrder);
app.MapGet("/orders/{id:guid}", GetOrder);

app.MapPost("/api/v1/promocodes/apply", ApplyPromoCode);
app.MapPost("/promocodes/apply", ApplyPromoCode);

app.MapGet("/api/v1/payment-methods", (IPaymentProviderAccessor accessor) =>
{
    var methods = PaymentMethodsData.GetAvailableMethods(accessor.GetAvailableMethods());
    return Results.Ok(methods);
});

app.MapPost("/api/v1/payments/{provider}", CreatePayment);
app.MapPost("/payments/{provider}", CreatePayment);

app.MapPost("/api/v1/webhooks/{provider}", HandleWebhook);
app.MapPost("/webhooks/{provider}", HandleWebhook);

await app.RunAsync();

static async Task<IResult> CreateOrder(CreateOrderRequest request, HttpContext httpContext, CreateOrderHandler handler, CancellationToken cancellationToken)
{
    ValidateUserAuthorization(httpContext, request.GameUserId);
    var result = await handler.HandleAsync(request, cancellationToken);
    return Results.Created($"/api/v1/orders/{result.OrderId}", result);
}

static async Task<IResult> GetOrder(Guid id, HttpContext httpContext, IOrderRepository repository, CancellationToken cancellationToken)
{
    var order = await repository.GetAsync(id, cancellationToken);
    if (order is null) return Results.NotFound();

    ValidateUserAuthorization(httpContext, order.GameUserId);
    return Results.Ok(order);
}

static async Task<IResult> ApplyPromoCode(ApplyPromoCodeRequest request, ApplyPromoCodeHandler handler, CancellationToken cancellationToken)
{
    var result = await handler.HandleAsync(request, cancellationToken);
    return Results.Ok(result);
}

static async Task<IResult> CreatePayment(
    string provider,
    CreatePaymentRequest request,
    CreatePaymentHandler handler,
    CancellationToken cancellationToken)
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
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

// Fail-closed webhook pipeline:
//   1. provider unknown → 400
//   2. provider not configured (no credentials) → 503
//   3. cryptographic/source verification fails inside ParseWebhookAsync → 401
//   4. valid signature but malformed payload → 400
// There is deliberately NO shared-secret bypass: every provider must pass its own verification.
static async Task<IResult> HandleWebhook(
    HttpContext httpContext,
    string provider,
    HandleWebhookHandler handler,
    IPaymentProviderAccessor accessor,
    CancellationToken cancellationToken)
{
    if (!TryParseProvider(provider, out var method))
    {
        return Results.BadRequest(new { error = "Unknown provider." });
    }

    if (method == DomainPaymentMethod.MockProvider && !httpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
    {
        return Results.NotFound();
    }

    var providerInstance = accessor.GetProvider(method);
    if (providerInstance is null)
    {
        return Results.Json(new { error = "Provider not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    using var reader = new StreamReader(httpContext.Request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);

    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var (key, values) in httpContext.Request.Headers)
    {
        headers[key] = values.ToString();
    }

    var envelope = new WebhookEnvelope(
        Body: body,
        Headers: headers,
        RemoteIp: httpContext.Connection.RemoteIpAddress?.ToString());

    WebhookResult? webhookResult;
    try
    {
        webhookResult = await providerInstance.ParseWebhookAsync(envelope, cancellationToken);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (NotImplementedException)
    {
        return Results.Json(new { error = "Webhook verification not implemented for this provider." }, statusCode: StatusCodes.Status501NotImplemented);
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or CryptographicException)
    {
        // Authenticity failure — do not leak details to the caller.
        return Results.Unauthorized();
    }
    catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException)
    {
        // Verified source but unusable payload — non-2xx makes the provider retry.
        return Results.BadRequest(new { error = "Invalid webhook payload." });
    }

    if (webhookResult is null)
    {
        // Authentic but non-terminal notification (e.g. waiting_for_capture) — acknowledge it.
        return Results.Ok(new { processed = false });
    }

    var request = new PaymentWebhookRequest(
        webhookResult.EventId,
        webhookResult.OrderId,
        webhookResult.PaymentId,
        webhookResult.Status,
        webhookResult.Amount,
        webhookResult.Currency);

    try
    {
        var processed = await handler.HandleAsync(method, request, cancellationToken);
        return Results.Ok(new { processed });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

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
        case "mock":
        case "mockprovider":
        case "mock_provider":
            method = Ordering.Domain.Payments.PaymentMethod.MockProvider;
            return true;
        default:
            return false;
    }
}

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
            throw new UnauthorizedAccessException("Access denied: You can only access your own orders.");
        }
    }
}

namespace Ordering.API
{
    public sealed class Program;
}
