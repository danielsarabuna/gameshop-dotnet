namespace Ordering.Application.Abstractions;

public interface IWebhookIdempotencyStore
{
    bool TryBegin(string provider, string eventId);
}

