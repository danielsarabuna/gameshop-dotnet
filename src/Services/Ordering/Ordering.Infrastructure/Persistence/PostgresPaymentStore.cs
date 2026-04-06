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
                           SELECT id, order_id, provider, status, external_id, created_at_utc, completed_at_utc
                           FROM payments
                           WHERE order_id = @OrderId AND provider = @Provider
                           ORDER BY created_at_utc DESC
                           LIMIT 1;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<PaymentRow>(
            new CommandDefinition(sql, new { OrderId = orderId, Provider = (int)provider }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new Payment(row.Id, row.OrderId, (PaymentMethod)row.Provider, (PaymentStatus)row.Status, row.ExternalId, row.CreatedAtUtc, row.CompletedAtUtc);
    }

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        const string sql = """
                           INSERT INTO payments
                               (id, order_id, provider, status, external_id, created_at_utc, completed_at_utc)
                           VALUES
                               (@Id, @OrderId, @Provider, @Status, @ExternalId, @CreatedAtUtc, @CompletedAtUtc);
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
            payment.CompletedAtUtc
        }, cancellationToken: cancellationToken));
    }

    public async Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE payments
                           SET status = @Status,
                               external_id = @ExternalId,
                               completed_at_utc = @CompletedAtUtc
                           WHERE id = @Id;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            payment.Id,
            Status = (int)payment.Status,
            payment.ExternalId,
            payment.CompletedAtUtc
        }, cancellationToken: cancellationToken));
    }

    private sealed record PaymentRow(
        Guid Id,
        Guid OrderId,
        int Provider,
        int Status,
        string? ExternalId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc);
}
