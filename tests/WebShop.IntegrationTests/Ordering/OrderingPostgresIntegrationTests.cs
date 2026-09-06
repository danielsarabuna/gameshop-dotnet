using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Application.Orders.CreateOrder;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using Ordering.Domain.Products;
using Ordering.Infrastructure.Persistence;

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
    public async Task Apply_promocode_GAME10_returns_10_percent_discount()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var client = _factory!.CreateClient();
        var payload = new
        {
            code = "GAME10",
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

    [SkippableFact]
    public async Task Concurrent_payment_reservations_create_one_database_row()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var orderId = Guid.NewGuid();
        using var scope = _factory!.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<IPaymentStore>();
        var attempts = Enumerable.Range(0, 20).Select(_ => payments.GetOrAddAsync(
            new Payment(Guid.NewGuid(), orderId, PaymentMethod.Stripe, PaymentStatus.Pending, null, DateTimeOffset.UtcNow, null),
            CancellationToken.None));

        var reservations = await Task.WhenAll(attempts);

        Assert.Single(reservations.Select(result => result.Payment.Id).Distinct());
        Assert.Single(reservations, result => result.Created);
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        Assert.Equal(1, await ScalarAsync<int>(connection,
            "SELECT count(*) FROM payments WHERE order_id = @orderId AND provider = @provider",
            ("orderId", orderId), ("provider", (int)PaymentMethod.Stripe)));
    }

    [SkippableFact]
    public async Task Concurrent_orders_cannot_overspend_promo_limit()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var code = $"ONCE{Guid.NewGuid():N}"[..20];
        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO promo_codes (code, type, value, currency, max_uses, used_count) VALUES (@code, 1, 10, 'EUR', 1, 0)",
                connection);
            command.Parameters.AddWithValue("code", code);
            await command.ExecuteNonQueryAsync();
        }

        var payload = new
        {
            gameUserId = "promo-race-user",
            paymentMethod = "Stripe",
            items = new[] { new { productId = DiamondPackId, quantity = 1 } },
            promoCode = code
        };
        var client = _factory!.CreateClient();
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/orders/create", payload),
            client.PostAsJsonAsync("/api/v1/orders/create", payload));

        Assert.Single(responses, response => response.IsSuccessStatusCode);
        Assert.Single(responses, response => response.StatusCode == System.Net.HttpStatusCode.BadRequest);
        await using var verify = new NpgsqlConnection(_fixture.ConnectionString);
        Assert.Equal(1, await ScalarAsync<int>(verify, "SELECT used_count FROM promo_codes WHERE code = @code", ("code", code)));
        Assert.Equal(1, await ScalarAsync<int>(verify, "SELECT count(*) FROM promo_redemptions WHERE code = @code", ("code", code)));
    }

    [SkippableFact]
    public async Task Expired_promo_reservation_is_released_and_fails_the_abandoned_order()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var code = $"TTL{Guid.NewGuid():N}"[..20];
        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO promo_codes (code, type, value, currency, max_uses, used_count) VALUES (@code, 1, 10, 'EUR', 1, 0)",
                connection);
            command.Parameters.AddWithValue("code", code);
            await command.ExecuteNonQueryAsync();
        }

        var create = await _factory!.CreateClient().PostAsJsonAsync("/api/v1/orders/create", new
        {
            gameUserId = "expired-promo-user",
            paymentMethod = "Stripe",
            items = new[] { new { productId = DiamondPackId, quantity = 1 } },
            promoCode = code
        });
        create.EnsureSuccessStatusCode();
        var orderId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetGuid();

        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "UPDATE promo_redemptions SET expires_at_utc = now() - interval '1 second' WHERE order_id = @id",
                connection);
            command.Parameters.AddWithValue("id", orderId);
            await command.ExecuteNonQueryAsync();
        }

        using var scope = _factory.Services.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IPromoReservationMaintenanceStore>();
        Assert.Equal(1, await maintenance.ReleaseExpiredAsync(CancellationToken.None));

        await using var verify = new NpgsqlConnection(_fixture.ConnectionString);
        Assert.Equal(0, await ScalarAsync<int>(verify, "SELECT used_count FROM promo_codes WHERE code = @code", ("code", code)));
        Assert.Equal(2, await ScalarAsync<int>(verify, "SELECT status FROM promo_redemptions WHERE order_id = @id", ("id", orderId)));
        Assert.Equal(2, await ScalarAsync<int>(verify, "SELECT status FROM orders WHERE id = @id", ("id", orderId)));
    }

    [SkippableFact]
    public async Task Successful_webhook_commits_payment_order_event_promo_and_outbox_together()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var code = $"PAY{Guid.NewGuid():N}"[..20];
        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO promo_codes (code, type, value, currency, max_uses, used_count) VALUES (@code, 1, 10, 'EUR', 1, 0)",
                connection);
            command.Parameters.AddWithValue("code", code);
            await command.ExecuteNonQueryAsync();
        }

        var create = await _factory!.CreateClient().PostAsJsonAsync("/api/v1/orders/create", new
        {
            gameUserId = "atomic-webhook-user",
            paymentMethod = "Stripe",
            items = new[] { new { productId = DiamondPackId, quantity = 1 } },
            promoCode = code
        });
        create.EnsureSuccessStatusCode();
        var body = await create.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = body.GetProperty("orderId").GetGuid();
        Assert.Equal(0.12m, body.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(1.11m, body.GetProperty("total").GetDecimal());
        var eventId = $"evt_{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<HandleWebhookHandler>();
        Assert.True(await handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest(eventId, orderId, Guid.NewGuid(), "succeeded", 1.11m, "EUR"),
            CancellationToken.None));

        await using var verify = new NpgsqlConnection(_fixture.ConnectionString);
        Assert.Equal(1, await ScalarAsync<int>(verify, "SELECT status FROM orders WHERE id = @id", ("id", orderId)));
        Assert.Equal(1, await ScalarAsync<int>(verify, "SELECT status FROM payments WHERE order_id = @id", ("id", orderId)));
        Assert.Equal(1, await ScalarAsync<int>(verify, "SELECT count(*) FROM webhook_events WHERE event_id = @event", ("event", eventId)));
        Assert.Equal(1, await ScalarAsync<int>(verify, "SELECT status FROM promo_redemptions WHERE order_id = @id", ("id", orderId)));
        Assert.Equal(2, await ScalarAsync<int>(verify, "SELECT count(*) FROM outbox_events WHERE payload->>'orderId' = @id", ("id", orderId.ToString())));
    }

    [SkippableFact]
    public async Task Outbox_lease_blocks_second_dispatcher_until_retry_releases_it()
    {
        Skip.IfNot(_fixture.IsAvailable, $"Docker unavailable: {_fixture.UnavailabilityReason}");

        var messageId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "INSERT INTO outbox_events (id, type, payload) VALUES (@id, 'lease-test', '{}'::jsonb)",
                connection);
            command.Parameters.AddWithValue("id", messageId);
            await command.ExecuteNonQueryAsync();
        }

        using var scope = _factory!.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var first = new PostgresOutboxStore(configuration, environment);
        var second = new PostgresOutboxStore(configuration, environment);
        var firstClaim = await first.ClaimBatchAsync(100, CancellationToken.None);
        var message = Assert.Single(firstClaim, item => item.Id == messageId);

        Assert.DoesNotContain(await second.ClaimBatchAsync(100, CancellationToken.None), item => item.Id == messageId);
        await first.RetryAsync(message, TimeSpan.Zero, "test retry", CancellationToken.None);
        var reclaimed = Assert.Single(await second.ClaimBatchAsync(100, CancellationToken.None), item => item.Id == messageId);
        await second.DeadLetterAsync(reclaimed, "poison test", CancellationToken.None);

        await using var verify = new NpgsqlConnection(_fixture.ConnectionString);
        Assert.Equal(1, await ScalarAsync<int>(verify,
            "SELECT count(*) FROM outbox_events WHERE id = @id AND dead_lettered_at_utc IS NOT NULL AND last_error = 'poison test'",
            ("id", messageId)));
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }
}
