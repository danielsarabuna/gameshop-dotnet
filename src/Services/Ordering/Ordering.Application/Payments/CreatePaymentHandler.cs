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

        var reservation = await _payments.GetOrAddAsync(new Payment(
            Id: Guid.NewGuid(),
            OrderId: orderId,
            Provider: provider,
            Status: PaymentStatus.Pending,
            ExternalId: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null), cancellationToken);

        if (!string.IsNullOrEmpty(reservation.Payment.ExternalId))
        {
            return new CreatePaymentResult(
                reservation.Payment.Id,
                reservation.Payment.Provider,
                reservation.Payment.Status,
                string.IsNullOrWhiteSpace(reservation.Payment.CheckoutUrl)
                    ? BuildCheckoutUrl(reservation.Payment)
                    : reservation.Payment.CheckoutUrl);
        }

        var paymentProvider = _providerAccessor.GetProvider(provider)
            ?? throw new InvalidOperationException($"Payment provider {provider} is not configured.");

        var intentResult = await paymentProvider.CreatePaymentIntentAsync(
            order.Total,
            order.Currency,
            orderId,
            $"payment:{provider}:{orderId:D}",
            cancellationToken);

        var payment = reservation.Payment with
        {
            Status = intentResult.Status,
            ExternalId = intentResult.ExternalId,
            CheckoutUrl = intentResult.CheckoutUrl
        };

        await _payments.UpdateAsync(payment, cancellationToken);

        return new CreatePaymentResult(payment.Id, payment.Provider, payment.Status, intentResult.CheckoutUrl);
    }

    private static string BuildCheckoutUrl(Payment payment) =>
        $"https://example.invalid/checkout/{payment.Provider.ToString().ToLowerInvariant()}/{payment.Id:D}";
}
