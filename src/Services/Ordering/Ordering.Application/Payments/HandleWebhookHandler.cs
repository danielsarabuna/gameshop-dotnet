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
    private readonly IPaymentWebhookStore _webhooks;

    public HandleWebhookHandler(
        IOrderRepository orders,
        IPaymentStore payments,
        IPaymentWebhookStore webhooks)
    {
        _orders = orders;
        _payments = payments;
        _webhooks = webhooks;
    }

    public async Task<bool> HandleAsync(PaymentMethod provider, PaymentWebhookRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            throw new ArgumentException("EventId is required.", nameof(request));
        }

        return await ProcessAsync(provider, request, cancellationToken);
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

            var outbox = new List<OutboxMessage>();
            if (order.Status != OrderStatus.Paid)
            {
                var effectivePaymentId = payment?.Id ?? paymentId;
                order.SetPaymentMethod(provider);
                order.MarkPaid(effectivePaymentId.ToString("D"), DateTimeOffset.UtcNow);
                outbox.AddRange([
                    new OutboxMessage(Guid.NewGuid(), OutboxMessageTypes.OrderCompleted,
                        JsonSerializer.Serialize(BuildCompletedEvent(order), PayloadOptions)),
                    new OutboxMessage(Guid.NewGuid(), OutboxMessageTypes.SupabaseOrderPaid,
                        JsonSerializer.Serialize(ToDelivery(order), PayloadOptions)),
                ]);
            }

            var updatedPayment = EnsurePayment(payment, order, provider, paymentId) with
            {
                Status = PaymentStatus.Succeeded,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
            return await CommitAsync(provider, request.EventId, true, order, updatedPayment, outbox, cancellationToken);
        }

        if (status is "failed" or "canceled" or "cancelled" or "error")
        {
            if (order.Status == OrderStatus.Paid)
            {
                var paidPayment = EnsurePayment(payment, order, provider, paymentId) with
                {
                    Status = PaymentStatus.Succeeded,
                    CompletedAtUtc = payment?.CompletedAtUtc ?? order.PaidAtUtc ?? DateTimeOffset.UtcNow
                };
                return await CommitAsync(provider, request.EventId, false, order, paidPayment, [], cancellationToken);
            }

            order.SetPaymentMethod(provider);
            order.MarkFailed("payment_failed");
            var storedPayment = EnsurePayment(payment, order, provider, paymentId);
            var updatedPayment = storedPayment with { Status = PaymentStatus.Failed, CompletedAtUtc = DateTimeOffset.UtcNow };
            return await CommitAsync(provider, request.EventId, false, order, updatedPayment, [], cancellationToken);
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

    private Task<bool> CommitAsync(
        PaymentMethod provider,
        string eventId,
        bool succeeded,
        Order order,
        Payment payment,
        IReadOnlyList<OutboxMessage> outbox,
        CancellationToken cancellationToken)
        => CommitCoreAsync(new PaymentWebhookCommit(
            provider.ToString(),
            eventId.Trim(),
            succeeded,
            order,
            payment,
            outbox), cancellationToken);

    private async Task<bool> CommitCoreAsync(PaymentWebhookCommit command, CancellationToken cancellationToken)
    {
        var result = await _webhooks.CommitAsync(command, cancellationToken);
        return result == PaymentWebhookCommitResult.Applied;
    }

    private static Payment EnsurePayment(
        Payment? existing,
        Order order,
        PaymentMethod provider,
        Guid fallbackPaymentId)
    {
        if (existing is not null)
        {
            return existing;
        }

        return new Payment(
            Id: fallbackPaymentId,
            OrderId: order.Id,
            Provider: provider,
            Status: PaymentStatus.Pending,
            ExternalId: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null);
    }
}
