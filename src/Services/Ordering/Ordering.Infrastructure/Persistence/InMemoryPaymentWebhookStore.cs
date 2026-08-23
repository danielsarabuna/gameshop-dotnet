using Ordering.Application.Abstractions;
using Ordering.Application.Payments;

namespace Ordering.Infrastructure.Persistence;

public sealed class InMemoryPaymentWebhookStore(
    IOrderRepository orders,
    IPaymentStore payments,
    IWebhookIdempotencyStore idempotency,
    IPromoCodeStore promoCodes) : IPaymentWebhookStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<PaymentWebhookCommitResult> CommitAsync(
        PaymentWebhookCommit command,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!idempotency.TryBegin(command.Provider, command.EventId))
            {
                return PaymentWebhookCommitResult.Duplicate;
            }

            try
            {
                if (command.Succeeded
                    && command.Outbox.Count > 0
                    && !string.IsNullOrWhiteSpace(command.Order.PromoCode)
                    && !await promoCodes.TryConsumeAsync(command.Order.PromoCode, cancellationToken))
                {
                    throw new InvalidOperationException("Promo code usage limit has been reached.");
                }

                await orders.UpdateWithOutboxAsync(command.Order, command.Outbox, cancellationToken);
                var reservation = await payments.GetOrAddAsync(command.Payment, cancellationToken);
                await payments.UpdateAsync(command.Payment with { Id = reservation.Payment.Id }, cancellationToken);
                return PaymentWebhookCommitResult.Applied;
            }
            catch
            {
                idempotency.Release(command.Provider, command.EventId);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
