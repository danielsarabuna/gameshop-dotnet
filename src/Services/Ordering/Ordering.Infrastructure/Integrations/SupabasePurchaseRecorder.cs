using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure.Integrations;

public sealed class SupabasePurchaseRecorder : IPurchaseRecorder
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SupabasePurchaseRecorder> _logger;

    public SupabasePurchaseRecorder(HttpClient http, IConfiguration configuration, ILogger<SupabasePurchaseRecorder> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RecordAsync(Order order, CancellationToken cancellationToken)
    {
        var supabaseUrl = _configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
        var serviceKey = _configuration["Supabase:ServiceRoleKey"] ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY");
        var table = _configuration["Supabase:PurchasesTable"] ?? "purchases";

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceKey))
        {
            return;
        }

        var endpoint = $"{supabaseUrl.TrimEnd('/')}/rest/v1/{table}";
        var rows = order.Items.Select(item => new
        {
            order_id = order.Id,
            game_user_id = order.GameUserId,
            payment_method = order.PaymentMethod.ToString(),
            currency = order.Currency,
            subtotal = order.Subtotal,
            discount_amount = order.DiscountAmount,
            total = order.Total,
            promo_code = order.PromoCode,
            product_id = item.ProductId,
            product_type = item.Type.ToString(),
            quantity = item.Quantity,
            unit_price = item.UnitPrice,
            metadata = item.Metadata,
            created_at_utc = order.CreatedAtUtc,
            paid_at_utc = order.PaidAtUtc
        }).ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(rows)
        };

        request.Headers.Add("apikey", serviceKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
        request.Headers.Add("Prefer", "return=minimal");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Supabase purchase write failed: {Status} {Body}", (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supabase purchase write failed.");
        }
    }
}
