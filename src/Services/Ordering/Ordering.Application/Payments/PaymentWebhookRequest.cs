namespace Ordering.Application.Payments;

public sealed record PaymentWebhookRequest(
    string EventId,
    Guid OrderId,
    Guid PaymentId,
    string Status
);

