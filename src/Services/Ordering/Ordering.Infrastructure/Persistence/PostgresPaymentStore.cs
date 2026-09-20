using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;

namespace Ordering.Infrastructure.Persistence;

public sealed class PostgresPaymentStore : IPaymentStore
{
    private readonly string _connectionString;

    public PostgresPaymentStore(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    public async Task<Payment?> GetByOrderAsync(Guid orderId, PaymentMethod provider, CancellationToken cancellationToken)
    {
        const string sql = """
                           SELECT id, order_id, provider, status, external_id, created_at_utc, completed_at_utc, checkout_url
                           FROM payments
                           WHERE order_id = @OrderId AND provider = @Provider
                           ORDER BY created_at_utc DESC
                           LIMIT 1;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<PaymentRow>(
            new CommandDefinition(sql, new { OrderId = orderId, Provider = (int)provider }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        return Map(row);
    }

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO payments
                               (id, order_id, provider, status, external_id, created_at_utc, completed_at_utc, checkout_url)
                           VALUES
                               (@Id, @OrderId, @Provider, @Status, @ExternalId, @CreatedAtUtc, @CompletedAtUtc, @CheckoutUrl);
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            payment.Id,
            payment.OrderId,
            Provider = (int)payment.Provider,
            Status = (int)payment.Status,
            payment.ExternalId,
            payment.CreatedAtUtc,
            payment.CompletedAtUtc,
            payment.CheckoutUrl
        }, cancellationToken: cancellationToken));
    }

    public async Task<PaymentReservation> GetOrAddAsync(Payment payment, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO payments
                               (id, order_id, provider, status, external_id, created_at_utc, completed_at_utc, checkout_url)
                           VALUES
                               (@Id, @OrderId, @Provider, @Status, @ExternalId, @CreatedAtUtc, @CompletedAtUtc, @CheckoutUrl)
                           ON CONFLICT (order_id, provider) DO UPDATE
                           SET order_id = EXCLUDED.order_id
                           RETURNING id, order_id, provider, status, external_id, created_at_utc,
                                     completed_at_utc, checkout_url, (xmax = 0) AS created;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<PaymentReservationRow>(new CommandDefinition(sql, new
        {
            payment.Id,
            payment.OrderId,
            Provider = (int)payment.Provider,
            Status = (int)payment.Status,
            payment.ExternalId,
            payment.CreatedAtUtc,
            payment.CompletedAtUtc,
            payment.CheckoutUrl
        }, cancellationToken: cancellationToken));

        return new PaymentReservation(Map(row), row.Created);
    }

    public async Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE payments
                           SET status = @Status,
                               external_id = @ExternalId,
                               completed_at_utc = @CompletedAtUtc,
                               checkout_url = COALESCE(@CheckoutUrl, checkout_url)
                           WHERE id = @Id;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            payment.Id,
            Status = (int)payment.Status,
            payment.ExternalId,
            payment.CompletedAtUtc,
            payment.CheckoutUrl
        }, cancellationToken: cancellationToken));
    }

    private sealed record PaymentRow(
        Guid Id,
        Guid OrderId,
        int Provider,
        int Status,
        string? ExternalId,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc,
        string? CheckoutUrl);

    private sealed record PaymentReservationRow(
        Guid Id,
        Guid OrderId,
        int Provider,
        int Status,
        string? ExternalId,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc,
        string? CheckoutUrl,
        bool Created);

    private static Payment Map(PaymentRow row)
    {
        var createdAt = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));
        var completedAt = row.CompletedAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.CompletedAtUtc.Value, DateTimeKind.Utc))
            : (DateTimeOffset?)null;
        return new Payment(row.Id, row.OrderId, (PaymentMethod)row.Provider, (PaymentStatus)row.Status, row.ExternalId, createdAt, completedAt, row.CheckoutUrl);
    }

    private static Payment Map(PaymentReservationRow row)
        => Map(new PaymentRow(row.Id, row.OrderId, row.Provider, row.Status, row.ExternalId, row.CreatedAtUtc, row.CompletedAtUtc, row.CheckoutUrl));
}
