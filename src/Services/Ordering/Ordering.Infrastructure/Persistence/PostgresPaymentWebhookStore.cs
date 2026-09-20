using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure.Persistence;

public sealed class PostgresPaymentWebhookStore : IPaymentWebhookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString;

    public PostgresPaymentWebhookStore(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    public async Task<PaymentWebhookCommitResult> CommitAsync(
        PaymentWebhookCommit command,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var insertedEvent = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            INSERT INTO webhook_events (provider, event_id, created_at_utc)
            VALUES (@Provider, @EventId, now())
            ON CONFLICT (provider, event_id) DO NOTHING
            RETURNING 1;
            """,
            new { command.Provider, command.EventId },
            transaction,
            cancellationToken: cancellationToken));
        if (insertedEvent is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PaymentWebhookCommitResult.Duplicate;
        }

        var orderState = await connection.QuerySingleOrDefaultAsync<OrderState>(new CommandDefinition(
            "SELECT status, failure_reason AS FailureReason FROM orders WHERE id = @Id FOR UPDATE;",
            new { command.Order.Id },
            transaction,
            cancellationToken: cancellationToken));
        if (orderState is null)
        {
            throw new ArgumentException("Order not found.", nameof(command));
        }

        if (command.Succeeded && orderState.Status == (int)OrderStatus.Failed)
        {
            // A provider can report a completed charge after checkout expiry. Keep the
            // released promo and failed order intact, but persist the charge for finance.
            await UpsertPaymentAsync(connection, transaction, command, cancellationToken);
            await InsertReconciliationCaseAsync(connection, transaction, command, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PaymentWebhookCommitResult.ReconciliationRequired;
        }

        var alreadyPaid = orderState.Status == (int)OrderStatus.Paid;
        if (!alreadyPaid)
        {
            if (command.Succeeded)
            {
                await ConsumePromoReservationAsync(connection, transaction, command.Order, cancellationToken);
            }
            else
            {
                await ReleasePromoReservationAsync(connection, transaction, command.Order.Id, cancellationToken);
            }

            await UpdateOrderAsync(connection, transaction, command.Order, cancellationToken);
            if (command.Succeeded && command.Outbox.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO outbox_events (id, type, payload, attempts, next_attempt_at, created_at_utc)
                    VALUES (@Id, @Type, CAST(@Payload AS jsonb), 0, now(), now())
                    ON CONFLICT (id) DO NOTHING;
                    """,
                    command.Outbox.Select(message => new { message.Id, message.Type, Payload = message.PayloadJson }),
                    transaction,
                    cancellationToken: cancellationToken));
            }
        }

        if (!alreadyPaid || command.Succeeded)
        {
            await UpsertPaymentAsync(connection, transaction, command, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return PaymentWebhookCommitResult.Applied;
    }

    private static Task InsertReconciliationCaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PaymentWebhookCommit command,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payment_reconciliation_cases (provider, event_id, order_id, payment_id, reason)
            VALUES (@Provider, @EventId, @OrderId, @PaymentId, 'late_success_after_failed_order')
            ON CONFLICT (provider, event_id) DO NOTHING;
            """,
            new
            {
                command.Provider,
                command.EventId,
                OrderId = command.Order.Id,
                PaymentId = command.Payment.Id
            }, transaction, cancellationToken: cancellationToken));

    private static async Task ConsumePromoReservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Order order,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.PromoCode))
        {
            return;
        }

        var consumed = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            UPDATE promo_redemptions
            SET status = 1, consumed_at_utc = now()
            WHERE order_id = @OrderId AND code = @Code AND status = 0
            RETURNING 1;
            """,
            new { OrderId = order.Id, Code = order.PromoCode },
            transaction,
            cancellationToken: cancellationToken));

        if (consumed is null)
        {
            var existingStatus = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT status FROM promo_redemptions WHERE order_id = @OrderId;",
                new { OrderId = order.Id },
                transaction,
                cancellationToken: cancellationToken));
            if (existingStatus is not null)
            {
                throw new InvalidOperationException("Promo code reservation was already released.");
            }

            var legacyReservation = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                """
                UPDATE promo_codes
                SET used_count = used_count + 1
                WHERE code = @Code
                  AND is_active = TRUE
                  AND (max_uses <= 0 OR used_count < max_uses)
                RETURNING 1;
                """,
                new { Code = order.PromoCode },
                transaction,
                cancellationToken: cancellationToken));
            if (legacyReservation is null)
            {
                throw new InvalidOperationException("Promo code usage limit has been reached.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO promo_redemptions
                    (order_id, code, status, reserved_at_utc, consumed_at_utc)
                VALUES
                    (@OrderId, @Code, 1, now(), now());
                """,
                new { OrderId = order.Id, Code = order.PromoCode },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private static async Task ReleasePromoReservationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var code = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            """
            UPDATE promo_redemptions
            SET status = 2, released_at_utc = now()
            WHERE order_id = @OrderId AND status = 0
            RETURNING code;
            """,
            new { OrderId = orderId },
            transaction,
            cancellationToken: cancellationToken));
        if (code is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE promo_codes SET used_count = GREATEST(0, used_count - 1) WHERE code = @Code;",
                new { Code = code },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private static Task UpdateOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Order order,
        CancellationToken cancellationToken)
        => connection.ExecuteAsync(new CommandDefinition(
            """
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
            """,
            new
            {
                order.Id,
                order.GameUserId,
                PaymentMethod = (int)order.PaymentMethod,
                Items = JsonSerializer.Serialize(order.Items, JsonOptions),
                order.Currency,
                order.DiscountAmount,
                order.PromoCode,
                Status = (int)order.Status,
                order.PaymentId,
                order.PaidAtUtc,
                order.FailureReason
            },
            transaction,
            cancellationToken: cancellationToken));

    private static Task UpsertPaymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PaymentWebhookCommit command,
        CancellationToken cancellationToken)
    {
        var payment = command.Payment;
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO payments
                (id, order_id, provider, status, external_id, created_at_utc, completed_at_utc, checkout_url)
            VALUES
                (@Id, @OrderId, @Provider, @Status, @ExternalId, @CreatedAtUtc, @CompletedAtUtc, @CheckoutUrl)
            ON CONFLICT (order_id, provider) DO UPDATE
            SET status = EXCLUDED.status,
                external_id = COALESCE(payments.external_id, EXCLUDED.external_id),
                completed_at_utc = EXCLUDED.completed_at_utc,
                checkout_url = COALESCE(payments.checkout_url, EXCLUDED.checkout_url);
            """,
            new
            {
                payment.Id,
                payment.OrderId,
                Provider = (int)payment.Provider,
                Status = (int)payment.Status,
                payment.ExternalId,
                payment.CreatedAtUtc,
                payment.CompletedAtUtc,
                payment.CheckoutUrl
            },
            transaction,
            cancellationToken: cancellationToken));
    }

    private sealed record OrderState(int Status, string? FailureReason);
}
