using System.Security.Cryptography;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using Stripe;
using Stripe.Checkout;
using PaymentIntent = Stripe.PaymentIntent;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public class StripePaymentProvider : IPaymentProvider
{
    private readonly StripeClient _client;
    private readonly string? _webhookSecret;
    private readonly string _successUrl;
    private readonly string _cancelUrl;

    public DomainPaymentMethod Provider => DomainPaymentMethod.Stripe;

    public StripePaymentProvider(
        string secretKey,
        string? webhookSecret = null,
        string? successUrl = null,
        string? cancelUrl = null)
    {
        _client = new StripeClient(secretKey);
        _webhookSecret = webhookSecret;
        // Hosted Checkout returns the player to these pages after paying / cancelling.
        _successUrl = successUrl ?? "https://GameShop.local/order/complete";
        _cancelUrl = cancelUrl ?? "https://GameShop.local/order/cancelled";
    }

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        // A bare PaymentIntent has NO payable URL; the redirect model requires a
        // hosted Checkout Session. checkout.session.completed is our primary
        // success signal (payment_intent.succeeded remains supported as backup).
        var sessionService = new SessionService(_client);
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = $"{_successUrl}?order_id={orderId:D}",
            CancelUrl = $"{_cancelUrl}?order_id={orderId:D}",
            ClientReferenceId = orderId.ToString("D"),
            Metadata = new Dictionary<string, string>
            {
                ["order_id"] = orderId.ToString("D")
            },
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = currency.ToLowerInvariant(),
                        UnitAmount = (long)(amount * 100),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"Order {orderId:D}"
                        }
                    }
                }
            ]
        }, cancellationToken: cancellationToken);

        return new PaymentIntentResult(
            ExternalId: session.Id,
            CheckoutUrl: session.Url,
            Status: MapStatus(session.Status)
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
            case Events.CheckoutSessionCompleted:
            {
                var session = stripeEvent.Data.Object as Session
                    ?? throw new InvalidOperationException($"Cannot deserialize {stripeEvent.Type} payload.");

                var orderId = ResolveOrderId(session.Metadata, session.ClientReferenceId);
                // Amount arrives in minor units; convert for order-total validation.
                var amount = session.AmountTotal.HasValue ? session.AmountTotal.Value / 100m : (decimal?)null;

                return Task.FromResult<WebhookResult?>(new WebhookResult(
                    OrderId: orderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: stripeEvent.Id,
                    Status: "succeeded",
                    Amount: amount,
                    Currency: string.IsNullOrWhiteSpace(session.Currency) ? null : session.Currency.ToUpperInvariant()));
            }
            case Events.PaymentIntentSucceeded:
            case Events.PaymentIntentPaymentFailed:
                var intent = stripeEvent.Data.Object as PaymentIntent
                    ?? throw new InvalidOperationException($"Cannot deserialize {stripeEvent.Type} payload.");

                var piOrderId = intent.Metadata is not null && intent.Metadata.TryGetValue("order_id", out var rawOrderId)
                    && Guid.TryParse(rawOrderId, out var parsedOrderId)
                        ? parsedOrderId
                        : throw new InvalidOperationException("Stripe event is missing metadata.order_id.");

                // Amount arrives in minor units; convert for order-total validation.
                var piAmount = intent.Amount / 100m;

                return Task.FromResult<WebhookResult?>(new WebhookResult(
                    OrderId: piOrderId,
                    PaymentId: Guid.NewGuid(),
                    EventId: stripeEvent.Id,
                    Status: stripeEvent.Type == Events.PaymentIntentSucceeded ? "succeeded" : "failed",
                    Amount: piAmount,
                    Currency: string.IsNullOrWhiteSpace(intent.Currency) ? null : intent.Currency.ToUpperInvariant()));

            default:
                // Authentic but non-terminal events are acknowledged without side effects.
                return Task.FromResult<WebhookResult?>(null);
        }
    }

    private static Guid ResolveOrderId(IReadOnlyDictionary<string, string>? metadata, string? clientReferenceId)
    {
        var raw = metadata is not null && metadata.TryGetValue("order_id", out var fromMetadata)
            ? fromMetadata
            : clientReferenceId;

        if (Guid.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException("Stripe event is missing order reference (metadata.order_id / client_reference_id).");
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
