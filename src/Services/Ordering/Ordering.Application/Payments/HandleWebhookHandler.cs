using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed class HandleWebhookHandler
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentStore _payments;
    private readonly IWebhookIdempotencyStore _idempotency;
    private readonly IPromoCodeStore _promoCodes;
    private readonly IPurchaseRecorder _purchases;

    public HandleWebhookHandler(
        IOrderRepository orders,
        IPaymentStore payments,
        IWebhookIdempotencyStore idempotency,
        IPromoCodeStore promoCodes,
        IPurchaseRecorder purchases)
    {
        _orders = orders;
        _payments = payments;
        _idempotency = idempotency;
        _promoCodes = promoCodes;
        _purchases = purchases;
    }

    public async Task<bool> HandleAsync(PaymentMethod provider, PaymentWebhookRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            throw new ArgumentException("EventId is required.", nameof(request));
        }

        if (!_idempotency.TryBegin(provider.ToString(), request.EventId.Trim()))
        {
            return false;
        }

        var order = await _orders.GetAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            throw new ArgumentException("Order not found.", nameof(request));
        }

        order.SetPaymentMethod(provider);

        var payment = await _payments.GetByOrderAsync(order.Id, provider, cancellationToken);
        if (payment is null)
        {
            payment = new Payment(
                Id: request.PaymentId,
                OrderId: order.Id,
                Provider: provider,
                Status: PaymentStatus.Pending,
                ExternalId: null,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: null
            );
            await _payments.AddAsync(payment, cancellationToken);
        }
        else if (payment.Id != request.PaymentId)
        {
            throw new ArgumentException("PaymentId mismatch.", nameof(request));
        }

        var status = request.Status.Trim().ToLowerInvariant();

        if (status is "succeeded" or "paid" or "success")
        {
            var wasPaid = order.Status == OrderStatus.Paid;
            order.MarkPaid(payment.Id.ToString("D"), DateTimeOffset.UtcNow);
            await _orders.UpdateAsync(order, cancellationToken);

            var updatedPayment = payment with { Status = PaymentStatus.Succeeded, CompletedAtUtc = DateTimeOffset.UtcNow };
            await _payments.UpdateAsync(updatedPayment, cancellationToken);

            if (!wasPaid && !string.IsNullOrWhiteSpace(order.PromoCode))
            {
                _promoCodes.TryConsume(order.PromoCode);
            }

            if (!wasPaid)
            {
                await _purchases.RecordAsync(order, cancellationToken);
            }

            return true;
        }

        if (status is "failed" or "canceled" or "cancelled" or "error")
        {
            order.MarkFailed("payment_failed");
            await _orders.UpdateAsync(order, cancellationToken);

            var updatedPayment = payment with { Status = PaymentStatus.Failed, CompletedAtUtc = DateTimeOffset.UtcNow };
            await _payments.UpdateAsync(updatedPayment, cancellationToken);

            return true;
        }

        throw new ArgumentException("Unknown payment status.", nameof(request));
    }
}
