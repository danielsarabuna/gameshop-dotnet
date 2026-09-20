using System.Collections.Concurrent;
using Ordering.Application.Abstractions;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure.Persistence;

public sealed class InMemoryOrderRepository : IOrderRepository, IOutboxDispatcherStore
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();
    private readonly ConcurrentQueue<OutboxMessage> _outbox = [];

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        _orders.TryAdd(order.Id, order);
        return Task.CompletedTask;
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return Task.FromResult(_orders.TryGetValue(id, out var order) ? order : null);
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken)
    {
        _orders.AddOrUpdate(order.Id, order, (_, _) => order);
        return Task.CompletedTask;
    }

    public Task UpdateWithOutboxAsync(Order order, IReadOnlyList<OutboxMessage> outbox, CancellationToken cancellationToken)
    {
        _orders.AddOrUpdate(order.Id, order, (_, _) => order);
        foreach (var message in outbox)
        {
            _outbox.Enqueue(message);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int max, CancellationToken cancellationToken)
    {
        var claimed = new List<OutboxMessage>(max);
        while (claimed.Count < max && _outbox.TryDequeue(out var message))
        {
            claimed.Add(message);
        }

        return Task.FromResult<IReadOnlyList<OutboxMessage>>(claimed);
    }

    public Task AckAsync(OutboxMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RetryAsync(OutboxMessage message, TimeSpan retryIn, string error, CancellationToken cancellationToken)
    {
        // In-memory queue has no delayed redelivery; re-enqueue with an incremented attempt
        // counter so the dispatcher's poison threshold still applies (dev-only mode).
        _outbox.Enqueue(message with { Attempts = message.Attempts + 1 });
        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(OutboxMessage message, string error, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
