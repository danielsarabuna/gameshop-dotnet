using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// Postgres-backed outbox queue for the dispatcher. Claim marks rows (attempts++) with
/// FOR UPDATE SKIP LOCKED so several instances could dispatch concurrently without double work.
/// </summary>
public sealed class PostgresOutboxStore : IOutboxDispatcherStore
{
    private const string ClaimSql = """
                                    WITH claimed AS (
                                        SELECT id
                                        FROM outbox_events
                                        WHERE processed_at_utc IS NULL AND next_attempt_at <= now()
                                        ORDER BY created_at_utc
                                        LIMIT @Max
                                        FOR UPDATE SKIP LOCKED
                                    )
                                    UPDATE outbox_events o
                                    SET attempts = o.attempts + 1
                                    FROM claimed c
                                    WHERE o.id = c.id
                                    RETURNING o.id, o.type, o.payload::text AS payload_json, o.attempts;
                                    """;

    private readonly string _connectionString;

    public PostgresOutboxStore(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int max, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<ClaimedRow>(new CommandDefinition(
            ClaimSql, new { Max = max }, cancellationToken: cancellationToken));

        return rows
            .Select(r => new OutboxMessage(r.Id, r.Type, r.PayloadJson, r.Attempts))
            .ToList();
    }

    public async Task AckAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE outbox_events SET processed_at_utc = now() WHERE id = @Id;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = message.Id }, cancellationToken: cancellationToken));
    }

    public async Task RetryAsync(OutboxMessage message, TimeSpan retryIn, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE outbox_events SET next_attempt_at = now() + @RetryIn WHERE id = @Id;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = message.Id, RetryIn = retryIn }, cancellationToken: cancellationToken));
    }

    private sealed record ClaimedRow(Guid Id, string Type, string PayloadJson, int Attempts);
}

/// <summary>Backoff policy shared by dispatcher implementations.</summary>
public static class OutboxBackoff
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(10);

    /// <summary>Capped exponential: 5s, 10s, 20s … up to 10 minutes.</summary>
    public static TimeSpan Next(int attempts)
        => TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, Math.Max(0, attempts - 1)), Ceiling.TotalSeconds));

    public const int PoisonThreshold = 20;
}
