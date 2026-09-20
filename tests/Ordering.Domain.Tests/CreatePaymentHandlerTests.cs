using System.Collections.Concurrent;
using Ordering.Application.Abstractions;
using Ordering.Application.Payments;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;
using Ordering.Domain.Products;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Domain.Tests;

public sealed class CreatePaymentHandlerTests
{
    [Fact]
    public async Task HandleAsync_ConcurrentRequests_ReturnSameReservedPayment()
    {
        var order = new Order(
            Guid.NewGuid(),
            "player-1",
            PaymentMethod.Stripe,
            [new OrderItem(Guid.NewGuid(), "Diamonds", ProductType.Currency, 9.99m, 1, new Dictionary<string, string>())],
            "EUR",
            0m,
            null,
            DateTimeOffset.UtcNow);
        var provider = new IdempotentProvider();
        var handler = new CreatePaymentHandler(
            new SingleOrderRepository(order),
            new InMemoryPaymentStore(),
            new SingleProviderAccessor(provider));

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => handler.HandleAsync(order.Id, PaymentMethod.Stripe, CancellationToken.None)));

        Assert.Single(results.Select(result => result.PaymentId).Distinct());
        Assert.Single(results.Select(result => result.CheckoutUrl).Distinct());
        Assert.Single(provider.IdempotencyKeys.Distinct());
    }

    private sealed class IdempotentProvider : IPaymentProvider
    {
        public ConcurrentBag<string> IdempotencyKeys { get; } = [];
        public PaymentMethod Provider => PaymentMethod.Stripe;

        public Task<PaymentIntentResult> CreatePaymentIntentAsync(
            decimal amount,
            string currency,
            Guid orderId,
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            IdempotencyKeys.Add(idempotencyKey);
            return Task.FromResult(new PaymentIntentResult(
                idempotencyKey,
                $"https://checkout.example/{Uri.EscapeDataString(idempotencyKey)}",
                PaymentStatus.Pending));
        }

        public Task<WebhookResult?> ParseWebhookAsync(WebhookEnvelope envelope, CancellationToken cancellationToken)
            => Task.FromResult<WebhookResult?>(null);
    }

    private sealed class SingleProviderAccessor(IPaymentProvider provider) : IPaymentProviderAccessor
    {
        public IPaymentProvider? GetProvider(PaymentMethod method) => method == provider.Provider ? provider : null;
        public IReadOnlyCollection<PaymentMethod> GetAvailableMethods() => [provider.Provider];
    }

    private sealed class SingleOrderRepository(Order order) : IOrderRepository
    {
        public Task AddAsync(Order value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(id == order.Id ? order : null);
        public Task UpdateAsync(Order value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateWithOutboxAsync(Order value, IReadOnlyList<OutboxMessage> outbox, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
