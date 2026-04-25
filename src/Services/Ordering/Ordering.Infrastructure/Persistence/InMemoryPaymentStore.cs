using System.Collections.Concurrent;
using Ordering.Application.Abstractions;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;

namespace Ordering.Infrastructure.Persistence;

public sealed class InMemoryPaymentStore : IPaymentStore
{
    private readonly ConcurrentDictionary<string, Payment> _payments = new();

    public Task<Payment?> GetByOrderAsync(Guid orderId, PaymentMethod provider, CancellationToken cancellationToken)
    {
        return Task.FromResult(_payments.TryGetValue(Key(orderId, provider), out var payment) ? payment : null);
    }

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        _payments.TryAdd(Key(payment.OrderId, payment.Provider), payment);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        _payments.AddOrUpdate(Key(payment.OrderId, payment.Provider), payment, (_, _) => payment);
        return Task.CompletedTask;
    }

    private static string Key(Guid orderId, PaymentMethod provider) => $"{orderId:D}:{provider}";
}

