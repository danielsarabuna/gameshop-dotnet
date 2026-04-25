using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed class CreatePaymentHandler
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentStore _payments;

    public CreatePaymentHandler(IOrderRepository orders, IPaymentStore payments)
    {
        _orders = orders;
        _payments = payments;
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
        if (existing is not null)
        {
            return new CreatePaymentResult(existing.Id, existing.Provider, existing.Status, BuildCheckoutUrl(existing));
        }

        var payment = new Payment(
            Id: Guid.NewGuid(),
            OrderId: orderId,
            Provider: provider,
            Status: PaymentStatus.Pending,
            ExternalId: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null
        );

        await _payments.AddAsync(payment, cancellationToken);

        return new CreatePaymentResult(payment.Id, payment.Provider, payment.Status, BuildCheckoutUrl(payment));
    }

    private static string BuildCheckoutUrl(Payment payment) =>
        $"https://checkout.GameShop.local/{payment.Provider.ToString().ToLowerInvariant()}/{payment.Id:D}";
}
