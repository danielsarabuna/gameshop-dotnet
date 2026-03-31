using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed record CreatePaymentResult(
    Guid PaymentId,
    PaymentMethod Provider,
    PaymentStatus Status,
    string CheckoutUrl
);

