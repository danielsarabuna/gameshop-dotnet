using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using DomainProductType = Ordering.Domain.Products.ProductType;

namespace Ordering.Infrastructure.Integrations;

/// <summary>
/// Atomic, idempotent delivery through a Supabase RPC. The database function writes the
/// grant row and audit lines in one transaction and never regresses delivered → paid.
/// </summary>
public sealed class SupabaseOrderDeliveryService : ISupabaseOrderDelivery
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SupabaseOrderDeliveryService> _logger;

    public SupabaseOrderDeliveryService(HttpClient http, IConfiguration configuration, ILogger<SupabaseOrderDeliveryService> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task DeliverAsync(SupabaseOrderDelivery delivery, CancellationToken cancellationToken)
    {
        var configured = TryGetConfig();
        if (configured is not { } config)
        {
            // Nothing to deliver to; treat as success so the outbox does not retry forever.
            return;
        }

        await RecordPaidOrderAsync(config, delivery, cancellationToken);
    }

    private (string Url, string Key)? TryGetConfig()
    {
        var supabaseUrl = _configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
        var serviceKey = _configuration["Supabase:ServiceRoleKey"] ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
        {
            return null;
        }

        return (supabaseUrl.TrimEnd('/'), serviceKey);
    }

    private async Task RecordPaidOrderAsync(
        (string Url, string Key) config,
        SupabaseOrderDelivery delivery,
        CancellationToken ct)
    {
        if (delivery.Items.Count == 0)
        {
            throw new InvalidOperationException($"Order {delivery.OrderId} has no items — cannot derive reward.");
        }

        // One delivery row per paid order. v2 carries each grant explicitly while the
        // legacy scalar fields remain accurate for homogeneous v1 orders.
        var first = delivery.Items[0];
        var rewardItems = delivery.Items.Select(item => new
        {
            product_id = item.ProductId,
            title = item.Title,
            kind = DeriveRewardKind(item),
            amount = DeriveRewardAmount(item),
            quantity = item.Quantity
        }).ToArray();
        var isBundle = delivery.Items.Select(DeriveRewardKind).Distinct(StringComparer.Ordinal).Count() > 1;
        var legacyPayload = new
        {
            p_order_id = delivery.OrderId,
            p_user_id = Guid.TryParse(delivery.GameUserId, out var userId)
                ? userId
                : throw new InvalidOperationException("Supabase delivery requires a UUID game user id."),
            p_product_id = first.ProductId,
            p_product_title = string.Join(" + ", delivery.Items.Select(i => i.Title)),
            p_currency_type = isBundle ? "Bundle" : DeriveCurrencyType(first),
            p_reward_amount = delivery.Items.Sum(DeriveRewardAmount),
            p_price = delivery.Total,
            p_price_currency = delivery.Currency,
            p_provider = delivery.Provider,
            p_provider_payment_id = delivery.ProviderPaymentId,
            p_paid_at = (delivery.PaidAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime,
            p_items = delivery.Items.Select(item => new
            {
                product_id = item.ProductId,
                product_type = item.ProductType,
                quantity = item.Quantity,
                unit_price = item.UnitPrice,
                metadata = item.Metadata
            }).ToArray()
        };
        var payload = new
        {
            legacyPayload.p_order_id,
            legacyPayload.p_user_id,
            legacyPayload.p_product_id,
            legacyPayload.p_product_title,
            legacyPayload.p_currency_type,
            legacyPayload.p_reward_amount,
            legacyPayload.p_price,
            legacyPayload.p_price_currency,
            legacyPayload.p_provider,
            legacyPayload.p_provider_payment_id,
            legacyPayload.p_paid_at,
            p_reward_items = rewardItems,
            legacyPayload.p_items
        };

        try
        {
            await PostRestAsync($"{config.Url}/rest/v1/rpc/record_webshop_paid_order_v2", config.Key, payload, ct);
        }
        catch (InvalidOperationException ex) when (!isBundle && ex.Message.Contains("PGRST202", StringComparison.Ordinal))
        {
            await PostRestAsync($"{config.Url}/rest/v1/rpc/record_webshop_paid_order", config.Key, legacyPayload, ct);
        }
        _logger.LogInformation("Supabase paid order recorded atomically for {OrderId}.", delivery.OrderId);
    }

    internal static string DeriveCurrencyType(SupabaseOrderItem item) => item.ProductType switch
    {
        nameof(DomainProductType.Currency) => "Diamonds",
        nameof(DomainProductType.Subscription) => "Subscription",
        _ => item.ProductType
    };

    internal static string DeriveRewardKind(SupabaseOrderItem item) => item.ProductType switch
    {
        nameof(DomainProductType.Currency) => "diamonds",
        nameof(DomainProductType.Subscription) => "subscription_days",
        _ => item.ProductType.ToLowerInvariant()
    };

    internal static int DeriveRewardAmount(SupabaseOrderItem item)
    {
        if (item.Metadata.TryGetValue("subscriptionDays", out var days)
            && int.TryParse(days, out var parsedDays))
        {
            return parsedDays * Math.Max(1, item.Quantity);
        }

        if (item.Metadata.TryGetValue("months", out var months)
            && int.TryParse(months, out var parsedMonths))
        {
            return parsedMonths * 30 * Math.Max(1, item.Quantity);
        }

        foreach (var key in (ReadOnlySpan<string>)["diamonds", "amount", "quantity"])
        {
            if (item.Metadata.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed))
            {
                return parsed * Math.Max(1, item.Quantity);
            }
        }

        return item.Quantity;
    }

    private async Task PostRestAsync(string endpoint, string serviceKey, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
        };

        request.Headers.Add("apikey", serviceKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Supabase REST write failed ({(int)response.StatusCode}): {text}");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Supabase REST write failed: {ex.Message}", ex);
        }
    }
}
