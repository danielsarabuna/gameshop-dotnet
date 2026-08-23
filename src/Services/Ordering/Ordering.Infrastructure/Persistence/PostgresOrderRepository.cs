using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;

namespace Ordering.Infrastructure.Persistence;

public sealed class PostgresOrderRepository : IOrderRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString;

    public PostgresOrderRepository(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO orders
                               (id, game_user_id, payment_method, items, currency, discount_amount, promo_code, created_at_utc, status, payment_id, paid_at_utc, failure_reason)
                           VALUES
                               (@Id, @GameUserId, @PaymentMethod, CAST(@Items AS jsonb), @Currency, @DiscountAmount, @PromoCode, @CreatedAtUtc, @Status, @PaymentId, @PaidAtUtc, @FailureReason);
                           """;

        var itemsJson = JsonSerializer.Serialize(order.Items, JsonOptions);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(order.PromoCode))
        {
            var reserved = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                """
                UPDATE promo_codes
                SET used_count = used_count + 1
                WHERE code = @Code
                  AND is_active = TRUE
                  AND (starts_at_utc IS NULL OR starts_at_utc <= now())
                  AND (expires_at_utc IS NULL OR expires_at_utc >= now())
                  AND (max_uses <= 0 OR used_count < max_uses)
                RETURNING 1;
                """,
                new { Code = order.PromoCode },
                transaction,
                cancellationToken: cancellationToken));
            if (reserved is null)
            {
                throw new ArgumentException("Promo code is invalid or its usage limit has been reached.", nameof(order));
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            order.Id,
            order.GameUserId,
            PaymentMethod = (int)order.PaymentMethod,
            Items = itemsJson,
            order.Currency,
            order.DiscountAmount,
            order.PromoCode,
            order.CreatedAtUtc,
            Status = (int)order.Status,
            order.PaymentId,
            order.PaidAtUtc,
            order.FailureReason
        }, transaction, cancellationToken: cancellationToken));

        if (!string.IsNullOrWhiteSpace(order.PromoCode))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO promo_redemptions (order_id, code, status, reserved_at_utc)
                VALUES (@OrderId, @Code, 0, now());
                """,
                new { OrderId = order.Id, Code = order.PromoCode },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, game_user_id, payment_method, items, currency, discount_amount, promo_code, created_at_utc,
                                  status, payment_id, paid_at_utc, failure_reason
                           FROM orders
                           WHERE id = @Id;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<OrderRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var items = JsonSerializer.Deserialize<IReadOnlyList<OrderItem>>(row.Items, JsonOptions) ?? Array.Empty<OrderItem>();
        var createdAt = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
        var order = new Order(
            row.Id,
            row.GameUserId,
            (PaymentMethod)row.PaymentMethod,
            items,
            row.Currency,
            row.DiscountAmount,
            row.PromoCode,
            createdAt);

        var status = (OrderStatus)row.Status;
        if (status == OrderStatus.Paid)
        {
            var paidAt = row.PaidAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.PaidAtUtc.Value, DateTimeKind.Utc))
                : DateTimeOffset.UtcNow;
            order.MarkPaid(row.PaymentId ?? "unknown", paidAt);
        }
        else if (status == OrderStatus.Failed)
        {
            order.MarkFailed(row.FailureReason ?? "failed");
        }

        return order;
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE orders
                           SET game_user_id = @GameUserId,
                               payment_method = @PaymentMethod,
                               items = CAST(@Items AS jsonb),
                               currency = @Currency,
                               discount_amount = @DiscountAmount,
                               promo_code = @PromoCode,
                               status = @Status,
                               payment_id = @PaymentId,
                               paid_at_utc = @PaidAtUtc,
                               failure_reason = @FailureReason
                           WHERE id = @Id;
                           """;

        var itemsJson = JsonSerializer.Serialize(order.Items, JsonOptions);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            order.Id,
            order.GameUserId,
            PaymentMethod = (int)order.PaymentMethod,
            Items = itemsJson,
            order.Currency,
            order.DiscountAmount,
            order.PromoCode,
            Status = (int)order.Status,
            order.PaymentId,
            order.PaidAtUtc,
            order.FailureReason
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdateWithOutboxAsync(Order order, IReadOnlyList<OutboxMessage> outbox, CancellationToken cancellationToken)
    {
        const string updateOrderSql = """
                                      UPDATE orders
                                      SET game_user_id = @GameUserId,
                                          payment_method = @PaymentMethod,
                                          items = CAST(@Items AS jsonb),
                                          currency = @Currency,
                                          discount_amount = @DiscountAmount,
                                          promo_code = @PromoCode,
                                          status = @Status,
                                          payment_id = @PaymentId,
                                          paid_at_utc = @PaidAtUtc,
                                          failure_reason = @FailureReason
                                      WHERE id = @Id;
                                      """;

        const string insertOutboxSql = """
                                       INSERT INTO outbox_events (id, type, payload, attempts, next_attempt_at, created_at_utc)
                                       VALUES (@Id, @Type, CAST(@Payload AS jsonb), 0, now(), now());
                                       """;

        var itemsJson = JsonSerializer.Serialize(order.Items, JsonOptions);
        var orderParams = new
        {
            order.Id,
            order.GameUserId,
            PaymentMethod = (int)order.PaymentMethod,
            Items = itemsJson,
            order.Currency,
            order.DiscountAmount,
            order.PromoCode,
            Status = (int)order.Status,
            order.PaymentId,
            order.PaidAtUtc,
            order.FailureReason
        };

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(updateOrderSql, orderParams, transaction, cancellationToken: cancellationToken));

        if (outbox.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                insertOutboxSql,
                outbox.Select(m => new { m.Id, m.Type, Payload = m.PayloadJson }),
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record OrderRow(
        Guid Id,
        string GameUserId,
        int PaymentMethod,
        string Items,
        string Currency,
        decimal DiscountAmount,
        string? PromoCode,
        DateTime CreatedAtUtc,
        int Status,
        string? PaymentId,
        DateTime? PaidAtUtc,
        string? FailureReason);
}
