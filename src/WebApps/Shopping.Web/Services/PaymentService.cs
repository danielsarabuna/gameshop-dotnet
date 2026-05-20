using System.Net.Http.Json;
using System.Text.Json;

namespace Shopping.Web.Services;

public class PaymentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(HttpClient http, ILogger<PaymentService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<PaymentMethodInfo>?> GetPaymentMethodsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("api/v1/payment-methods", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to get payment methods: {Status}", response.StatusCode);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<List<PaymentMethodInfo>>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment methods");
            return null;
        }
    }

    public async Task<PaymentResult?> CreatePaymentAsync(Guid orderId, string provider, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/v1/payments/{provider}", new { orderId }, cancellationToken: ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Payment creation failed: {Status}", response.StatusCode);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<PaymentResult>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment");
            return null;
        }
    }
}

public record PaymentMethodInfo(string Code, string Name, string? IconUrl);
public record PaymentResult(Guid PaymentId, string Provider, string Status, string CheckoutUrl);