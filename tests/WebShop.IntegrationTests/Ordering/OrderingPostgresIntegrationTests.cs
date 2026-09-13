using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Application.Orders.CreateOrder;
using Ordering.Domain.Payments;
using Ordering.Domain.Products;

namespace WebShop.IntegrationTests.OrderingApi;

[Trait("Category", "Integration")]
public sealed class OrderingPostgresIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly Guid DiamondPackId = Guid.Parse("d1a00000-0000-0000-0000-000000000060");
    private static readonly Guid PremiumMonthId = Guid.Parse("9aa00000-0000-0000-0000-000000000001");

    private readonly PostgresFixture _fixture;
    private OrderingPostgresFactory? _factory;

    public OrderingPostgresIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        if (!_fixture.IsAvailable)
        {
            return Task.CompletedTask;
        }

        _factory = new OrderingPostgresFactory(_fixture.ConnectionString);
        _factory.Catalog
            .Add(new CatalogProduct(
                DiamondPackId, "60 diamonds", "diamond pack",
                ProductType.Currency, 1.23m, "EUR", true,
                new Dictionary<string, string> { ["diamonds"] = "60" }))
            .Add(new CatalogProduct(
                PremiumMonthId, "Premium 1 month", "subscription",
                ProductType.Subscription, 6.99m, "EUR", true,
                new Dictionary<string, string> { ["months"] = "1" }));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task Payment_methods_returns_only_configured_providers()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var client = _factory!.CreateClient();

        var response = await client.GetAsync("/api/v1/payment-methods");
        response.EnsureSuccessStatusCode();

        var methods = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, methods.GetArrayLength());
        var codes = methods.EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
        Assert.Equal(["stripe"], codes);
    }

    [SkippableFact]
    public async Task Apply_promocode_LOVE10_returns_10_percent_discount()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var client = _factory!.CreateClient();
        var payload = new
        {
            code = "LOVE10",
            items = new[] { new { productId = DiamondPackId, quantity = 10 } }
        };

        var response = await client.PostAsJsonAsync("/api/v1/promocodes/apply", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isValid").GetBoolean());
        // 10 × 1.23 = 12.30 subtotal; 10% = 1.23 discount; 11.07 total
        Assert.Equal(12.30m, body.GetProperty("subtotal").GetDecimal());
        Assert.Equal(1.23m, body.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(11.07m, body.GetProperty("total").GetDecimal());
    }

    [SkippableFact]
    public async Task Create_order_persists_in_postgres_and_get_round_trips()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var client = _factory!.CreateClient();
        var payload = new
        {
            gameUserId = "test-user-001",
            paymentMethod = "Stripe",
            items = new[]
            {
                new { productId = DiamondPackId, quantity = 2 }
            },
            promoCode = (string?)null
        };

        var createResp = await client.PostAsJsonAsync("/api/v1/orders/create", payload);
        createResp.EnsureSuccessStatusCode();

        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("orderId").GetGuid();
        Assert.NotEqual(Guid.Empty, orderId);
        Assert.Equal(2.46m, created.GetProperty("subtotal").GetDecimal());
        Assert.Equal("Pending", created.GetProperty("status").GetString());

        // Round-trip via API
        var getResp = await client.GetAsync($"/api/v1/orders/{orderId}");
        getResp.EnsureSuccessStatusCode();
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test-user-001", fetched.GetProperty("gameUserId").GetString());
        Assert.Equal("Stripe", fetched.GetProperty("paymentMethod").GetString());
        Assert.Single(fetched.GetProperty("items").EnumerateArray());

        // Verify directly in Postgres that the row landed in the orders table
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT game_user_id, payment_method, currency, status FROM orders WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Order row not found in Postgres");
        Assert.Equal("test-user-001", reader.GetString(0));
        Assert.Equal((int)PaymentMethod.Stripe, reader.GetInt32(1));
        Assert.Equal("EUR", reader.GetString(2));
        Assert.Equal(0, reader.GetInt32(3)); // OrderStatus.Pending
    }

    [SkippableFact]
    public async Task Create_order_v1_rejects_mixed_reward_types()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var response = await _factory!.CreateClient().PostAsJsonAsync("/api/v1/orders/create", new
        {
            gameUserId = "test-user-001",
            paymentMethod = "Stripe",
            items = new[]
            {
                new { productId = DiamondPackId, quantity = 1 },
                new { productId = PremiumMonthId, quantity = 1 }
            }
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task Create_order_v2_accepts_mixed_reward_types()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        using var scope = _factory!.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateOrderHandler>();
        var result = await handler.HandleAsync(
            new CreateOrderRequest(
                "test-user-001",
                PaymentMethod.Stripe,
                [new CreateOrderLine(DiamondPackId, 1), new CreateOrderLine(PremiumMonthId, 1)],
                null),
            new CatalogScope("global", "global", "global", 2),
            CancellationToken.None);

        Assert.Equal(8.22m, result.Total);
    }
}
