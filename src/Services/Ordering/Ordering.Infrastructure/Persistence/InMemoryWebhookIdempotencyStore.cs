using System.Collections.Concurrent;
using Ordering.Application.Abstractions;

namespace Ordering.Infrastructure.Persistence;

public sealed class InMemoryWebhookIdempotencyStore : IWebhookIdempotencyStore
{
    private readonly ConcurrentDictionary<string, byte> _processed = new();

    public bool TryBegin(string provider, string eventId)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        return _processed.TryAdd($"{provider}:{eventId}", 0);
    }
}

