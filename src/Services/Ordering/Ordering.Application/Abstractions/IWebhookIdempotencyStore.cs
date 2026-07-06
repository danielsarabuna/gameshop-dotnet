namespace Ordering.Application.Abstractions;

public interface IWebhookIdempotencyStore
{
    /// <summary>Reserves the event id; false when it was already handled.</summary>
    bool TryBegin(string provider, string eventId);

    /// <summary>
    /// Gives the reservation back after a processing failure, so the provider's retry
    /// is not swallowed as a duplicate. Must be called before rethrowing any exception
    /// that occurred between TryBegin and the durable order update.
    /// </summary>
    void Release(string provider, string eventId);
}
