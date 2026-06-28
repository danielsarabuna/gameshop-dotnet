using System.Security.Cryptography;
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

    public Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            throw new InvalidOperationException("Stripe webhook secret is not configured.");
        }

        if (!envelope.Headers.TryGetValue("Stripe-Signature", out var signature))
        {
            throw new UnauthorizedAccessException("Missing Stripe-Signature header.");
        }

        // Official SDK check: HMAC-SHA256 over (timestamp + body) with replay-window tolerance.
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(envelope.Body, signature, _webhookSecret);
        }
        catch (StripeException ex)
        {
            throw new CryptographicException("Stripe signature verification failed.", ex);
        }

        switch (stripeEvent.Type)
        {
            case Events.PaymentIntentSucceeded:
            case Events.PaymentIntentPaymentFailed:
                var intent = stripeEvent.Data.Object as PaymentIntent
                    ?? throw new InvalidOperationException($"Cannot deserialize {stripeEvent.Type} payload.");

                var orderId = intent.Metadata is not null && intent.Metadata.TryGetValue("order_id", out var rawOrderId)
                    && Guid.TryParse(rawOrderId, out var parsedOrderId)
                        ? parsedOrderId
                        : throw new InvalidOperationException("Stripe event is missing metadata.order_id.");

                // Amount arrives in minor units; convert for order-total validation.
                var amount = intent.Amount / 100m;

                return Task.FromResult<WebhookResult?>(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: stripeEvent.Id,
                    Status: stripeEvent.Type == Events.PaymentIntentSucceeded ? "succeeded" : "failed",
                    Amount: amount,
                    Currency: string.IsNullOrWhiteSpace(intent.Currency) ? null : intent.Currency.ToUpperInvariant()));

            default:
                // Authentic but non-terminal events are acknowledged without side effects.
                return Task.FromResult<WebhookResult?>(null);
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
