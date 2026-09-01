using Ordering.Application.Payments;
using Ordering.Domain.Payments;

namespace Ordering.Application.Abstractions;

public interface IPaymentStore
{
    Task<Payment?> GetByOrderAsync(Guid orderId, PaymentMethod provider, CancellationToken cancellationToken);
    Task<PaymentReservation> GetOrAddAsync(Payment payment, CancellationToken cancellationToken);
    Task AddAsync(Payment payment, CancellationToken cancellationToken);
    Task UpdateAsync(Payment payment, CancellationToken cancellationToken);
}

public sealed record PaymentReservation(Payment Payment, bool Created);
