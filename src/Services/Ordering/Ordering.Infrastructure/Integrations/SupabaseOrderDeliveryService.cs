using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using DomainProductType = Ordering.Domain.Products.ProductType;

namespace Ordering.Infrastructure.Integrations;

/// <summary>
/// Idempotent delivery: both tables are written with resolution=merge-duplicates so outbox
/// retries converge. Requires purchases UNIQUE(order_id, product_id); see docs/payments.
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

        await UpsertGameOrdersAsync(config, delivery, cancellationToken);
        await UpsertPurchasesAuditAsync(config, delivery, cancellationToken);
    }

    private (string Url, string Key, string OrdersTable, string PurchasesTable)? TryGetConfig()
    {
        var supabaseUrl = _configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
        var serviceKey = _configuration["Supabase:ServiceRoleKey"] ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
        {
            return null;
        }

        return (
            supabaseUrl.TrimEnd('/'),
            serviceKey,
            _configuration["Supabase:OrdersTable"] ?? "webshop_orders",
            _configuration["Supabase:PurchasesTable"] ?? "purchases");
    }

    private async Task UpsertGameOrdersAsync(
        (string Url, string Key, string OrdersTable, string PurchasesTable) config,
        SupabaseOrderDelivery delivery,
        CancellationToken ct)
    {
        if (delivery.Items.Count == 0)
        {
            throw new InvalidOperationException($"Order {delivery.OrderId} has no items — cannot derive reward.");
        }

        // Game contract (see GameShop WebShopOrderRow + reserve/complete RPCs):
        // one row per ORDER whose id equals the order uuid; reward fields aggregate all lines.
        var first = delivery.Items[0];
        var row = new
        {
            id = delivery.OrderId,
            user_id = Guid.TryParse(delivery.GameUserId, out var userId) ? userId : (object)delivery.GameUserId,
            product_id = first.ProductId,
            product_title = string.Join(" + ", delivery.Items.Select(i => i.Title)),
            currency_type = DeriveCurrencyType(first),
            reward_amount = delivery.Items.Sum(DeriveRewardAmount),
            price = delivery.Total,
            price_currency = delivery.Currency,
            provider = delivery.Provider,
            provider_payment_id = delivery.ProviderPaymentId,
            status = "paid",
            paid_at = (delivery.PaidAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime
        };

        await PostRestAsync($"{config.Url}/rest/v1/{config.OrdersTable}", config.Key, row,
            prefer: "resolution=merge-duplicates", ct);
        _logger.LogInformation("Supabase webshop_orders upserted for order {OrderId}.", delivery.OrderId);
    }

    private async Task UpsertPurchasesAuditAsync(
        (string Url, string Key, string OrdersTable, string PurchasesTable) config,
        SupabaseOrderDelivery delivery,
        CancellationToken ct)
    {
        var rows = delivery.Items.Select(item => new
        {
            order_id = delivery.OrderId,
            game_user_id = delivery.GameUserId,
            payment_method = delivery.Provider,
            currency = delivery.Currency,
            total = delivery.Total,
            product_id = item.ProductId,
            product_type = item.ProductType,
            quantity = item.Quantity,
            unit_price = item.UnitPrice,
            metadata = item.Metadata,
            paid_at_utc = delivery.PaidAtUtc
        }).ToArray();

        await PostRestAsync($"{config.Url}/rest/v1/{config.PurchasesTable}", config.Key, rows,
            prefer: "resolution=merge-duplicates", ct);
    }

    internal static string DeriveCurrencyType(SupabaseOrderItem item) => item.ProductType switch
    {
        nameof(DomainProductType.Currency) => "Diamonds",
        nameof(DomainProductType.Subscription) => "Subscription",
        _ => item.ProductType
    };

    internal static int DeriveRewardAmount(SupabaseOrderItem item)
    {
        foreach (var key in (ReadOnlySpan<string>)["diamonds", "months", "amount", "quantity"])
        {
            if (item.Metadata.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed))
            {
                return parsed * Math.Max(1, item.Quantity);
            }
        }

        return item.Quantity;
    }

    private async Task PostRestAsync(string endpoint, string serviceKey, object body, string prefer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
        };

        request.Headers.Add("apikey", serviceKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("Prefer", prefer);

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
