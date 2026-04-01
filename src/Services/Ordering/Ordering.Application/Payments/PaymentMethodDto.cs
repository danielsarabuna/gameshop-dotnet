using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed record PaymentMethodDto(
    string Code,
    string Name,
    string? IconUrl
);

public static class PaymentMethodsData
{
    public static IReadOnlyList<PaymentMethodDto> GetAvailableMethods()
    {
        return
        [
            new PaymentMethodDto("stripe", "Stripe", null),
            new PaymentMethodDto("paypal", "PayPal", null),
            new PaymentMethodDto("yookassa", "YooKassa", null),
            new PaymentMethodDto("corvuspay", "CorvusPay", null),
            new PaymentMethodDto("xsolla", "Xsolla", null)
        ];
    }
}