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

    public async Task<ServiceCallResult<CreateOrderResult>> CreateOrderAsync(CreateOrderPayload payload, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/v1/orders/create", payload, cancellationToken: ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, ct) ?? "Order creation failed.";
                _logger.LogError("Order creation failed: {Status} {Error}", response.StatusCode, error);
                return ServiceCallResult<CreateOrderResult>.Failure(error);
            }

            var result = await response.Content.ReadFromJsonAsync<CreateOrderResult>(JsonOptions, ct);
            return result is null
                ? ServiceCallResult<CreateOrderResult>.Failure("Order creation returned an empty response.")
                : ServiceCallResult<CreateOrderResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            return ServiceCallResult<CreateOrderResult>.Failure("Could not create order.");
        }
    }

    public async Task<ServiceCallResult<PaymentResult>> CreatePaymentAsync(Guid orderId, string provider, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"api/v1/payments/{provider}", new { orderId }, cancellationToken: ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, ct) ?? "Payment creation failed.";
                _logger.LogError("Payment creation failed: {Status} {Error}", response.StatusCode, error);
                return ServiceCallResult<PaymentResult>.Failure(error);
            }

            var result = await response.Content.ReadFromJsonAsync<PaymentResult>(JsonOptions, ct);
            return result is null
                ? ServiceCallResult<PaymentResult>.Failure("Payment creation returned an empty response.")
                : ServiceCallResult<PaymentResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment");
            return ServiceCallResult<PaymentResult>.Failure("Could not create payment.");
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, ct);
            return string.IsNullOrWhiteSpace(error?.Error) ? null : error.Error;
        }
        catch
        {
            return null;
        }
    }
}

public record PaymentMethodInfo(string Code, string Name, string? IconUrl);
public record CreateOrderPayload(string GameUserId, string PaymentMethod, IReadOnlyList<CreateOrderLinePayload> Items, string? PromoCode);
public record CreateOrderLinePayload(Guid ProductId, int Quantity);
public record CreateOrderResult(Guid OrderId, string Status, decimal Subtotal, decimal DiscountAmount, decimal Total, string Currency);
public record PaymentResult(Guid PaymentId, string Provider, string Status, string CheckoutUrl);
public record ServiceCallResult<T>(bool IsSuccess, T? Value, string? Error)
{
    public static ServiceCallResult<T> Success(T value) => new(true, value, null);
    public static ServiceCallResult<T> Failure(string error) => new(false, default, error);
}

internal sealed record ApiError(string? Error);
