using EventBus;
using IntegrationEvents;
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
    private readonly IEventBus _events;

    public HandleWebhookHandler(
        IOrderRepository orders,
        IPaymentStore payments,
        IWebhookIdempotencyStore idempotency,
        IPromoCodeStore promoCodes,
        IPurchaseRecorder purchases,
        IEventBus events)
    {
        _orders = orders;
        _payments = payments;
        _idempotency = idempotency;
        _promoCodes = promoCodes;
        _purchases = purchases;
        _events = events;
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
        var paymentId = request.PaymentId == Guid.Empty ? Guid.NewGuid() : request.PaymentId;
        if (payment is null)
        {
            payment = new Payment(
                Id: paymentId,
                OrderId: order.Id,
                Provider: provider,
                Status: PaymentStatus.Pending,
                ExternalId: null,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: null
            );
            await _payments.AddAsync(payment, cancellationToken);
        }

        var status = request.Status.Trim().ToLowerInvariant();

        if (status is "succeeded" or "paid" or "success")
        {
            // Defense-in-depth: providers that report the charged amount must match the order
            // exactly (protects against partial-payment and currency-swap exploits).
            if (request.Amount is { } reportedAmount
                && (reportedAmount != order.Total
                    || !string.Equals(request.Currency, order.Currency, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"Webhook amount {reportedAmount} {request.Currency} does not match order total {order.Total} {order.Currency}.",
                    nameof(request));
            }

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

                var paidAtUtc = order.PaidAtUtc ?? DateTimeOffset.UtcNow;
                var completed = new OrderCompleted(
                    Id: Guid.NewGuid(),
                    OccurredAtUtc: paidAtUtc,
                    OrderId: order.Id,
                    GameUserId: order.GameUserId,
                    Total: order.Total,
                    Currency: order.Currency,
                    PaymentId: order.PaymentId ?? payment.Id.ToString("D"),
                    PaidAtUtc: paidAtUtc);
                await _events.PublishAsync(completed, cancellationToken);
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
