namespace Ordering.Domain.Payments;

public enum PaymentMethod
{
    Unspecified = 0,
    Stripe = 1,
    PayPal = 2,
    YooKassa = 3
}
