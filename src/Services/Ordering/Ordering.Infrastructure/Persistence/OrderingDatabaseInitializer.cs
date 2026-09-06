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

                           WITH ranked AS (
                               SELECT p.id,
                                      row_number() OVER (
                                          PARTITION BY p.order_id, p.provider
                                          ORDER BY (o.payment_id = p.id::text) DESC, p.created_at_utc DESC, p.id DESC
                                      ) AS row_number
                               FROM payments p
                               LEFT JOIN orders o ON o.id = p.order_id
                           )
                           DELETE FROM payments p
                           USING ranked r
                           WHERE p.id = r.id AND r.row_number > 1;

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

                           ALTER TABLE outbox_events ADD COLUMN IF NOT EXISTS locked_until_utc timestamptz NULL;
                           ALTER TABLE outbox_events ADD COLUMN IF NOT EXISTS locked_by text NULL;
                           ALTER TABLE outbox_events ADD COLUMN IF NOT EXISTS dead_lettered_at_utc timestamptz NULL;
                           ALTER TABLE outbox_events ADD COLUMN IF NOT EXISTS last_error text NULL;

                           CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders (created_at_utc);
                           DROP INDEX IF EXISTS idx_payments_order_provider;
                           CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_order_provider ON payments (order_id, provider);
                           DROP INDEX IF EXISTS ix_outbox_pending;
                           CREATE INDEX ix_outbox_pending
                               ON outbox_events (next_attempt_at)
                               WHERE processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL;

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

                           CREATE TABLE IF NOT EXISTS promo_redemptions (
                               order_id uuid PRIMARY KEY REFERENCES orders(id) ON DELETE CASCADE,
                               code text NOT NULL REFERENCES promo_codes(code),
                               status int NOT NULL DEFAULT 0,
                               reserved_at_utc timestamptz NOT NULL DEFAULT now(),
                               expires_at_utc timestamptz NOT NULL DEFAULT now() + interval '30 minutes',
                               consumed_at_utc timestamptz NULL,
                               released_at_utc timestamptz NULL
                           );

                           ALTER TABLE promo_redemptions ADD COLUMN IF NOT EXISTS expires_at_utc timestamptz NULL;
                           UPDATE promo_redemptions
                           SET expires_at_utc = reserved_at_utc + interval '30 minutes'
                           WHERE expires_at_utc IS NULL AND status = 0;

                           CREATE INDEX IF NOT EXISTS ix_promo_redemptions_code_status
                               ON promo_redemptions (code, status);
                           CREATE INDEX IF NOT EXISTS ix_promo_redemptions_expiry
                               ON promo_redemptions (expires_at_utc)
                               WHERE status = 0;
                           """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(PostgresPromoCodeStore.SeedSql(), cancellationToken: cancellationToken));
        _logger.LogInformation("Ordering database schema ensured.");
    }
}
