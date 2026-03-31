using Ordering.Domain.Orders;

namespace Ordering.Application.Abstractions;

public interface IPurchaseRecorder
{
    Task RecordAsync(Order order, CancellationToken cancellationToken);
}

