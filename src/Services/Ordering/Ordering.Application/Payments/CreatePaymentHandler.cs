using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed class CreatePaymentHandler
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentStore _payments;
    private readonly IPaymentProviderAccessor _providerAccessor;

    public CreatePaymentHandler(IOrderRepository orders, IPaymentStore payments, IPaymentProviderAccessor providerAccessor)
    {
        _orders = orders;
        _payments = payments;
        _providerAccessor = providerAccessor;
    }

    public async Task<CreatePaymentResult> HandleAsync(Guid orderId, PaymentMethod provider, CancellationToken cancellationToken)
    {
        var order = await _orders.GetAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new ArgumentException("Order not found.", nameof(orderId));
        }

        if (order.Status != OrderStatus.Pending)
        {
            throw new ArgumentException("Order is not pending.", nameof(orderId));
        }

        var methodWasUnset = order.PaymentMethod == PaymentMethod.Unspecified;
        order.SetPaymentMethod(provider);
        if (methodWasUnset)
        {
            await _orders.UpdateAsync(order, cancellationToken);
        }

        var existing = await _payments.GetByOrderAsync(orderId, provider, cancellationToken);
        if (existing is not null && !string.IsNullOrEmpty(existing.ExternalId))
        {
            // Idempotent re-entry (double click / page reload): hand back the SAME
            // payment with its real hosted-checkout URL, never a fabricated link.
            return new CreatePaymentResult(existing.Id, existing.Provider, existing.Status,
                string.IsNullOrWhiteSpace(existing.CheckoutUrl) ? BuildCheckoutUrl(existing) : existing.CheckoutUrl);
        }

        var paymentProvider = _providerAccessor.GetProvider(provider)
            ?? throw new InvalidOperationException($"Payment provider {provider} is not configured.");

        var intentResult = await paymentProvider.CreatePaymentIntentAsync(
            order.Total,
            order.Currency,
            orderId,
            cancellationToken);

        var payment = new Payment(
            Id: Guid.NewGuid(),
            OrderId: orderId,
            Provider: provider,
            Status: intentResult.Status,
            ExternalId: intentResult.ExternalId,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null,
            CheckoutUrl: intentResult.CheckoutUrl
        );

        await _payments.AddAsync(payment, cancellationToken);

        return new CreatePaymentResult(payment.Id, payment.Provider, payment.Status, intentResult.CheckoutUrl);
    }

    private static string BuildCheckoutUrl(Payment payment) =>
        $"https://checkout.GameShop.local/{payment.Provider.ToString().ToLowerInvariant()}/{payment.Id:D}";
}
