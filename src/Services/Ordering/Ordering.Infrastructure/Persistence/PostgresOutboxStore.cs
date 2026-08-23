using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// Postgres-backed outbox queue. A durable lease prevents multiple instances from dispatching
/// the same row after the short claim transaction has completed.
/// </summary>
public sealed class PostgresOutboxStore : IOutboxDispatcherStore
{
    private const string ClaimSql = """
                                    WITH claimed AS (
                                        SELECT id
                                        FROM outbox_events
                                        WHERE processed_at_utc IS NULL
                                          AND dead_lettered_at_utc IS NULL
                                          AND next_attempt_at <= now()
                                          AND (locked_until_utc IS NULL OR locked_until_utc <= now())
                                        ORDER BY created_at_utc
                                        LIMIT @Max
                                        FOR UPDATE SKIP LOCKED
                                    )
                                    UPDATE outbox_events o
                                    SET attempts = o.attempts + 1,
                                        locked_by = @WorkerId,
                                        locked_until_utc = now() + @LeaseDuration
                                    FROM claimed c
                                    WHERE o.id = c.id
                                    RETURNING o.id, o.type, o.payload::text AS payload_json, o.attempts;
                                    """;

    private readonly string _connectionString;
    private readonly string _workerId = Guid.NewGuid().ToString("N");
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public PostgresOutboxStore(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int max, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<ClaimedRow>(new CommandDefinition(
            ClaimSql, new { Max = max, WorkerId = _workerId, LeaseDuration }, cancellationToken: cancellationToken));

        return rows
            .Select(r => new OutboxMessage(r.Id, r.Type, r.PayloadJson, r.Attempts))
            .ToList();
    }

    public async Task AckAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE outbox_events
                           SET processed_at_utc = now(), locked_by = NULL, locked_until_utc = NULL
                           WHERE id = @Id AND locked_by = @WorkerId;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = message.Id, WorkerId = _workerId }, cancellationToken: cancellationToken));
    }

    public async Task RetryAsync(OutboxMessage message, TimeSpan retryIn, string error, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE outbox_events
                           SET next_attempt_at = now() + @RetryIn,
                               locked_by = NULL,
                               locked_until_utc = NULL,
                               last_error = @Error
                           WHERE id = @Id AND locked_by = @WorkerId;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = message.Id,
            RetryIn = retryIn,
            WorkerId = _workerId,
            Error = Truncate(error)
        }, cancellationToken: cancellationToken));
    }

    public async Task DeadLetterAsync(OutboxMessage message, string error, CancellationToken cancellationToken)
    {
        const string sql = """
                           UPDATE outbox_events
                           SET dead_lettered_at_utc = now(),
                               locked_by = NULL,
                               locked_until_utc = NULL,
                               last_error = @Error
                           WHERE id = @Id AND locked_by = @WorkerId;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = message.Id,
            WorkerId = _workerId,
            Error = Truncate(error)
        }, cancellationToken: cancellationToken));
    }

    private static string Truncate(string error) => error.Length <= 4000 ? error : error[..4000];

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
