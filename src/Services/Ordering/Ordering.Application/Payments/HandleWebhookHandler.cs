using System.Text.Json;
using EventBus;
using IntegrationEvents;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed class HandleWebhookHandler
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private readonly IOrderRepository _orders;
    private readonly IPaymentStore _payments;
    private readonly IWebhookIdempotencyStore _idempotency;
    private readonly IPromoCodeStore _promoCodes;

    public HandleWebhookHandler(
        IOrderRepository orders,
        IPaymentStore payments,
        IWebhookIdempotencyStore idempotency,
        IPromoCodeStore promoCodes)
    {
        _orders = orders;
        _payments = payments;
        _idempotency = idempotency;
        _promoCodes = promoCodes;
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

        try
        {
            return await ProcessAsync(provider, request, cancellationToken);
        }
        catch (ArgumentException)
        {
            // Permanent business rejection (unknown order/status, amount mismatch):
            // consume the slot and answer 400 so the provider stops retrying.
            throw;
        }
        catch
        {
            // Transient failure — the effect was NOT durably applied. Release the slot
            // so the provider's retry is not swallowed as a duplicate.
            _idempotency.Release(provider.ToString(), request.EventId.Trim());
            throw;
        }
    }

    private async Task<bool> ProcessAsync(PaymentMethod provider, PaymentWebhookRequest request, CancellationToken cancellationToken)
    {
        var order = await _orders.GetAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            throw new ArgumentException("Order not found.", nameof(request));
        }

        var payment = await _payments.GetByOrderAsync(order.Id, provider, cancellationToken);
        var paymentId = request.PaymentId == Guid.Empty ? Guid.NewGuid() : request.PaymentId;

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
            if (!wasPaid)
            {
                // The authoritative payment id is the one already recorded for this order+provider.
                var effectivePaymentId = payment?.Id ?? paymentId;
                order.SetPaymentMethod(provider);
                order.MarkPaid(effectivePaymentId.ToString("D"), DateTimeOffset.UtcNow);

                // Atomic: order state + both deferred side effects land together or not at all.
                // The dispatcher later publishes OrderCompleted to RabbitMQ and delivers to Supabase.
                await _orders.UpdateWithOutboxAsync(order,
                [
                    new OutboxMessage(Guid.NewGuid(), OutboxMessageTypes.OrderCompleted,
                        JsonSerializer.Serialize(BuildCompletedEvent(order), PayloadOptions)),
                    new OutboxMessage(Guid.NewGuid(), OutboxMessageTypes.SupabaseOrderPaid,
                        JsonSerializer.Serialize(ToDelivery(order), PayloadOptions)),
                ], cancellationToken);

                var storedPayment = await EnsurePaymentAsync(payment, order, provider, paymentId, cancellationToken);
                var updatedPayment = storedPayment with { Status = PaymentStatus.Succeeded, CompletedAtUtc = DateTimeOffset.UtcNow };
                await _payments.UpdateAsync(updatedPayment, cancellationToken);

                if (!string.IsNullOrWhiteSpace(order.PromoCode))
                {
                    await _promoCodes.TryConsumeAsync(order.PromoCode, cancellationToken);
                }
            }
            else
            {
                // Duplicate success notification for an already-paid order — acknowledge.
                var storedPayment = await EnsurePaymentAsync(payment, order, provider, paymentId, cancellationToken);
                var updatedPayment = storedPayment with { Status = PaymentStatus.Succeeded, CompletedAtUtc = DateTimeOffset.UtcNow };
                await _payments.UpdateAsync(updatedPayment, cancellationToken);
            }

            return true;
        }

        if (status is "failed" or "canceled" or "cancelled" or "error")
        {
            if (order.Status == OrderStatus.Paid)
            {
                // Never regress a paid order because of a late failure notification.
                return true;
            }

            order.SetPaymentMethod(provider);
            order.MarkFailed("payment_failed");
            await _orders.UpdateWithOutboxAsync(order, [], cancellationToken);

            var storedPayment = await EnsurePaymentAsync(payment, order, provider, paymentId, cancellationToken);
            var updatedPayment = storedPayment with { Status = PaymentStatus.Failed, CompletedAtUtc = DateTimeOffset.UtcNow };
            await _payments.UpdateAsync(updatedPayment, cancellationToken);

            return true;
        }

        throw new ArgumentException("Unknown payment status.", nameof(request));
    }

    private static OrderCompleted BuildCompletedEvent(Order order)
    {
        var paidAtUtc = order.PaidAtUtc ?? DateTimeOffset.UtcNow;
        return new OrderCompleted(
            Id: Guid.NewGuid(),
            OccurredAtUtc: paidAtUtc,
            OrderId: order.Id,
            GameUserId: order.GameUserId,
            Total: order.Total,
            Currency: order.Currency,
            PaymentId: order.PaymentId ?? string.Empty,
            PaidAtUtc: paidAtUtc);
    }

    private static SupabaseOrderDelivery ToDelivery(Order order)
        => new(
            OrderId: order.Id,
            GameUserId: order.GameUserId,
            Provider: order.PaymentMethod.ToString(),
            ProviderPaymentId: order.PaymentId,
            Total: order.Total,
            Currency: order.Currency,
            CreatedAtUtc: order.CreatedAtUtc,
            PaidAtUtc: order.PaidAtUtc,
            Items: order.Items.Select(i => new SupabaseOrderItem(
                i.ProductId, i.Title, i.Type.ToString(), i.Quantity, i.UnitPrice, i.Metadata)).ToList());

    private async Task<Payment> EnsurePaymentAsync(
        Payment? existing,
        Order order,
        PaymentMethod provider,
        Guid fallbackPaymentId,
        CancellationToken cancellationToken)
    {
        if (existing is not null)
        {
            return existing;
        }

        var created = new Payment(
            Id: fallbackPaymentId,
            OrderId: order.Id,
            Provider: provider,
            Status: PaymentStatus.Pending,
            ExternalId: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null);
        await _payments.AddAsync(created, cancellationToken);
        return created;
    }
}
