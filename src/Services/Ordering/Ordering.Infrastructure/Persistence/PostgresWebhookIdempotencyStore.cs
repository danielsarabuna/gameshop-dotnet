using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Persistence;

public sealed class PostgresWebhookIdempotencyStore : IWebhookIdempotencyStore
{
    private readonly string _connectionString;

    public PostgresWebhookIdempotencyStore(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    public bool TryBegin(string provider, string eventId)
    {
        const string sql = """
                           INSERT INTO webhook_events (provider, event_id, created_at_utc)
                           VALUES (@Provider, @EventId, @CreatedAtUtc)
                           ON CONFLICT (provider, event_id) DO NOTHING;
                           """;

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var rows = connection.Execute(sql, new
        {
            Provider = provider,
            EventId = eventId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        return rows > 0;
    }

    public void Release(string provider, string eventId)
    {
        const string sql = "DELETE FROM webhook_events WHERE provider = @Provider AND event_id = @EventId;";

        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        connection.Execute(sql, new { Provider = provider, EventId = eventId });
    }
}
