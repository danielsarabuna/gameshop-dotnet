using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ordering.Infrastructure.Persistence;

public sealed class OrderingDatabaseInitializer
{
    static OrderingDatabaseInitializer()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    private readonly string _connectionString;
    private readonly ILogger<OrderingDatabaseInitializer> _logger;

    public OrderingDatabaseInitializer(IConfiguration configuration, IHostEnvironment environment, ILogger<OrderingDatabaseInitializer> logger)
    {
        _logger = logger;
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
                           CREATE TABLE IF NOT EXISTS orders (
                               id uuid PRIMARY KEY,
                               game_user_id text NOT NULL,
                               payment_method int NOT NULL,
                               items jsonb NOT NULL,
                               currency text NOT NULL,
                               discount_amount numeric NOT NULL,
                               promo_code text NULL,
                               created_at_utc timestamptz NOT NULL,
                               status int NOT NULL,
                               payment_id text NULL,
                               paid_at_utc timestamptz NULL,
                               failure_reason text NULL
                           );

                           CREATE TABLE IF NOT EXISTS payments (
                               id uuid PRIMARY KEY,
                               order_id uuid NOT NULL,
                               provider int NOT NULL,
                               status int NOT NULL,
                               external_id text NULL,
                               created_at_utc timestamptz NOT NULL,
                               completed_at_utc timestamptz NULL
                           );

                           CREATE TABLE IF NOT EXISTS webhook_events (
                               provider text NOT NULL,
                               event_id text NOT NULL,
                               created_at_utc timestamptz NOT NULL,
                               PRIMARY KEY (provider, event_id)
                           );

                           CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders (created_at_utc);
                           CREATE INDEX IF NOT EXISTS idx_payments_order_provider ON payments (order_id, provider);
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        _logger.LogInformation("Ordering database schema ensured.");
    }
}
