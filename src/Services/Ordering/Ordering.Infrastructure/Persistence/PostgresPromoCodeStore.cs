using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Ordering.Application.Abstractions;
using Ordering.Application.PromoCodes;

namespace Ordering.Infrastructure.Persistence;

/// <summary>
/// Postgres promo code store. Consume is a conditional UPDATE (atomic), so usage limits hold
/// across restarts and concurrent instances.
/// </summary>
public sealed class PostgresPromoCodeStore : IPromoCodeStore
{
    private const string GetSql = """
                                  SELECT code, type, value, currency, is_active, starts_at_utc, expires_at_utc,
                                         max_uses, used_count, product_ids::text AS product_ids
                                  FROM promo_codes
                                  WHERE code = @Code;
                                  """;

    private const string ConsumeSql = """
                                      UPDATE promo_codes
                                      SET used_count = used_count + 1
                                      WHERE code = @Code
                                        AND is_active
                                        AND (max_uses <= 0 OR used_count < max_uses)
                                      RETURNING used_count;
                                      """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;

    public PostgresPromoCodeStore(IConfiguration configuration, IHostEnvironment environment)
    {
        _connectionString = PostgresConnectionFactory.GetConnectionString(configuration, environment);
    }

    /// <summary>Idempotent seed of the default campaign codes; executed by OrderingDatabaseInitializer.</summary>
    internal static string SeedSql()
    {
        var builder = new System.Text.StringBuilder();

        foreach (var promo in InMemoryPromoCodeStore.DefaultCodes())
        {
            var startsAt = promo.StartsAtUtc is null ? "NULL" : $"'{promo.StartsAtUtc:O}'::timestamptz";
            var expiresAt = promo.ExpiresAtUtc is null ? "NULL" : $"'{promo.ExpiresAtUtc:O}'::timestamptz";
            var productIds = promo.ProductIds.Count == 0
                ? "NULL"
                : $"CAST('{JsonSerializer.Serialize(promo.ProductIds, JsonOptions)}' AS jsonb)";
            var value = promo.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

            builder.AppendLine($"""
                                INSERT INTO promo_codes (code, type, value, currency, is_active, starts_at_utc, expires_at_utc, max_uses, used_count, product_ids)
                                VALUES ('{promo.Code}', {(int)promo.Type}, {value}, '{promo.Currency}', TRUE, {startsAt}, {expiresAt}, {promo.MaxUses}, 0, {productIds})
                                ON CONFLICT (code) DO NOTHING;
                                """);
        }

        return builder.ToString();
    }

    public async Task<PromoCode?> GetAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<PromoRow>(
            new CommandDefinition(GetSql, new { Code = code.Trim() }, cancellationToken: cancellationToken));
        return row is null ? null : Map(row);
    }

    public async Task<bool> TryConsumeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(
            new CommandDefinition(ConsumeSql, new { Code = code.Trim() }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    private static PromoCode Map(PromoRow row)
        => new(
            Code: row.Code,
            Type: (DiscountType)row.Type,
            Value: row.Value,
            Currency: row.Currency ?? "EUR",
            IsActive: row.IsActive,
            StartsAtUtc: row.StartsAtUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.StartsAtUtc.Value, DateTimeKind.Utc)) : null,
            ExpiresAtUtc: row.ExpiresAtUtc.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(row.ExpiresAtUtc.Value, DateTimeKind.Utc)) : null,
            MaxUses: row.MaxUses,
            UsedCount: row.UsedCount,
            ProductIds: string.IsNullOrEmpty(row.ProductIds)
                ? []
                : JsonSerializer.Deserialize<HashSet<Guid>>(row.ProductIds, JsonOptions) ?? []);

    private sealed record PromoRow(
        string Code,
        int Type,
        decimal Value,
        string? Currency,
        bool IsActive,
        DateTime? StartsAtUtc,
        DateTime? ExpiresAtUtc,
        int MaxUses,
        int UsedCount,
        string? ProductIds);
}
