using Ordering.Application.Payments;
using Ordering.Domain.Orders;

namespace Ordering.Application.Abstractions;

public interface IPaymentWebhookStore
{
    Task<PaymentWebhookCommitResult> CommitAsync(PaymentWebhookCommit command, CancellationToken cancellationToken);
}

public sealed record PaymentWebhookCommit(
    string Provider,
    string EventId,
    bool Succeeded,
    Order Order,
    Payment Payment,
    IReadOnlyList<OutboxMessage> Outbox);

public enum PaymentWebhookCommitResult
{
    Applied,
    Duplicate
}
