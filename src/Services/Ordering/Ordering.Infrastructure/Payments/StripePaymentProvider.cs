using System.Text;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using Stripe;
using PaymentIntent = Stripe.PaymentIntent;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class StripePaymentProvider : IPaymentProvider
{
    private readonly StripeClient _client;
    private readonly string? _webhookSecret;

    public DomainPaymentMethod Provider => DomainPaymentMethod.Stripe;

    public StripePaymentProvider(string secretKey, string? webhookSecret = null)
    {
        _client = new StripeClient(secretKey);
        _webhookSecret = webhookSecret;
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var service = new PaymentIntentService(_client);

        var intent = await service.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100),
            Currency = currency.ToLowerInvariant(),
            Metadata = new Dictionary<string, string>
            {
                ["order_id"] = orderId.ToString("D")
            }
        }, cancellationToken: cancellationToken);

        return new PaymentIntentResult(
            ExternalId: intent.Id,
            CheckoutUrl: $"https://pay.stripe.com/c/{intent.Id}",
            Status: MapStatus(intent.Status)
        );
    }

    public Task<WebhookResult> ParseWebhookAsync(Stream body, string? signature, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            throw new InvalidOperationException("Webhook secret not configured.");
        }

        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        var payload = reader.ReadToEnd();

        try
        {
            var json = System.Text.Json.JsonDocument.Parse(payload);
            var root = json.RootElement;

            var eventType = root.GetProperty("type").GetString();

            if (eventType == "payment_intent.succeeded")
            {
                var data = root.GetProperty("data").GetProperty("object");
                var paymentIntentId = data.GetProperty("id").GetString();
                var amount = data.GetProperty("amount").GetInt64();
                var metadata = data.GetProperty("metadata");

                Guid orderId;
                if (metadata.TryGetProperty("order_id", out var orderIdElement))
                {
                    orderId = Guid.Parse(orderIdElement.GetString()!);
                }
                else
                {
                    orderId = Guid.NewGuid();
                }

                return Task.FromResult(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: paymentIntentId ?? Guid.NewGuid().ToString(),
                    Status: "succeeded"
                ));
            }

            if (eventType == "payment_intent.payment_failed")
            {
                var data = root.GetProperty("data").GetProperty("object");
                var paymentIntentId = data.GetProperty("id").GetString();
                var metadata = data.GetProperty("metadata");

                Guid orderId;
                if (metadata.TryGetProperty("order_id", out var orderIdElement))
                {
                    orderId = Guid.Parse(orderIdElement.GetString()!);
                }
                else
                {
                    orderId = Guid.NewGuid();
                }

                return Task.FromResult(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: paymentIntentId ?? Guid.NewGuid().ToString(),
                    Status: "failed"
                ));
            }

            throw new InvalidOperationException($"Unhandled event type: {eventType}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse Stripe webhook: {ex.Message}");
        }
    }

    private static DomainPaymentStatus MapStatus(string? stripeStatus) => stripeStatus switch
    {
        "requires_payment_method" => DomainPaymentStatus.Pending,
        "requires_confirmation" => DomainPaymentStatus.Pending,
        "requires_action" => DomainPaymentStatus.Pending,
        "processing" => DomainPaymentStatus.Pending,
        "succeeded" => DomainPaymentStatus.Succeeded,
        "canceled" => DomainPaymentStatus.Failed,
        _ => DomainPaymentStatus.Pending
    };
}