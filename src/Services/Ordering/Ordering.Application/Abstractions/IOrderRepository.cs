using Ordering.Domain.Orders;

namespace Ordering.Application.Abstractions;

/// <summary>A deferred side-effect (event publication / external delivery) persisted atomically with the order.</summary>
public sealed record OutboxMessage(Guid Id, string Type, string PayloadJson, int Attempts = 0);

public static class OutboxMessageTypes
{
    /// <summary>Payload: IntegrationEvents.OrderCompleted serialized as JSON.</summary>
    public const string OrderCompleted = "order_completed.v1";

    /// <summary>Payload: SupabaseOrderDelivery serialized as JSON.</summary>
    public const string SupabaseOrderPaid = "supabase.order_paid.v1";
}

public interface IOutboxDispatcherStore
{
    /// <summary>Atomically claims up to <paramref name="max"/> pending messages (at-least-once).</summary>
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(int max, CancellationToken cancellationToken);

    Task AckAsync(OutboxMessage message, CancellationToken cancellationToken);

    /// <summary>Returns a failed message to the queue after <paramref name="retryIn"/> backoff.</summary>
    Task RetryAsync(OutboxMessage message, TimeSpan retryIn, CancellationToken cancellationToken);
}

/// <summary>
/// Persists the updated aggregate together with its outbox messages in ONE transaction
/// (Postgres), so a crash can never produce a paid order whose effects were never queued.
/// </summary>
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(Order order, CancellationToken cancellationToken);

    Task UpdateWithOutboxAsync(Order order, IReadOnlyList<OutboxMessage> outbox, CancellationToken cancellationToken);
}

public interface ISupabaseOrderDelivery
{
    /// <summary>Upserts the paid order into webshop_orders (game granting contract) and appends webshop_purchases audit rows.</summary>
    Task DeliverAsync(SupabaseOrderDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>Delivery contract describing a paid order to the game backend (Supabase webshop_orders / webshop_purchases).</summary>
public sealed record SupabaseOrderDelivery(
    Guid OrderId,
    string GameUserId,
    string Provider,
    string? ProviderPaymentId,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PaidAtUtc,
    IReadOnlyList<SupabaseOrderItem> Items);

public sealed record SupabaseOrderItem(
    Guid ProductId,
    string Title,
    string ProductType,
    int Quantity,
    decimal UnitPrice,
    IReadOnlyDictionary<string, string> Metadata);
