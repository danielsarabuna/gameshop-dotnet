using Ordering.Application.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;
using DomainPaymentStatus = Ordering.Application.Payments.PaymentStatus;

namespace Ordering.Infrastructure.Payments;

public sealed class MockPaymentProvider : IPaymentProvider
{
    public DomainPaymentMethod Provider => DomainPaymentMethod.MockProvider;

    public Task<PaymentIntentResult> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var mockExternalId = $"mock_tx_{Guid.NewGuid():N}";
        var mockCheckoutUrl = $"/order/mock-checkout?orderId={orderId}&tx={mockExternalId}";

        return Task.FromResult(new PaymentIntentResult(
            ExternalId: mockExternalId,
            CheckoutUrl: mockCheckoutUrl,
            Status: DomainPaymentStatus.Succeeded
        ));
    }

    public Task<WebhookResult> ParseWebhookAsync(Stream body, string? signature, CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid();
        return Task.FromResult(new WebhookResult(
            OrderId: orderId,
            PaymentId: Guid.NewGuid(),
            EventId: $"mock_evt_{Guid.NewGuid():N}",
            Status: "succeeded"
        ));
    }
}
