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

                           ALTER TABLE payments ADD COLUMN IF NOT EXISTS checkout_url text NULL;

                           CREATE TABLE IF NOT EXISTS webhook_events (
                               provider text NOT NULL,
                               event_id text NOT NULL,
                               created_at_utc timestamptz NOT NULL,
                               PRIMARY KEY (provider, event_id)
                           );

                           CREATE TABLE IF NOT EXISTS outbox_events (
                               id uuid PRIMARY KEY,
                               type text NOT NULL,
                               payload jsonb NOT NULL,
                               attempts int NOT NULL DEFAULT 0,
                               next_attempt_at timestamptz NOT NULL DEFAULT now(),
                               created_at_utc timestamptz NOT NULL DEFAULT now(),
                               processed_at_utc timestamptz NULL
                           );

                           CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders (created_at_utc);
                           CREATE INDEX IF NOT EXISTS idx_payments_order_provider ON payments (order_id, provider);
                           CREATE INDEX IF NOT EXISTS ix_outbox_pending
                               ON outbox_events (next_attempt_at) WHERE processed_at_utc IS NULL;

                           CREATE TABLE IF NOT EXISTS promo_codes (
                               code text PRIMARY KEY,
                               type int NOT NULL,
                               value numeric NOT NULL,
                               currency text NULL,
                               is_active boolean NOT NULL DEFAULT TRUE,
                               starts_at_utc timestamptz NULL,
                               expires_at_utc timestamptz NULL,
                               max_uses int NOT NULL DEFAULT 0,
                               used_count int NOT NULL DEFAULT 0,
                               product_ids jsonb NULL
                           );
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(PostgresPromoCodeStore.SeedSql(), cancellationToken: cancellationToken));
        _logger.LogInformation("Ordering database schema ensured.");
    }
}
