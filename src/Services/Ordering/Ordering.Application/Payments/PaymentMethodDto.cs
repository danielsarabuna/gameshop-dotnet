using Ordering.Domain.Payments;

namespace Ordering.Application.Payments;

public sealed record PaymentMethodDto(
    string Code,
    string Name,
    string? IconUrl
);

public static class PaymentMethodsData
{
    public static IReadOnlyList<PaymentMethodDto> GetAvailableMethods(IEnumerable<PaymentMethod> methods)
    {
        return methods
            .Where(method => method != PaymentMethod.Unspecified)
            .Distinct()
            .OrderBy(method => (int)method)
            .Select(method => method switch
            {
                PaymentMethod.Stripe => new PaymentMethodDto("stripe", "Stripe", null),
                PaymentMethod.PayPal => new PaymentMethodDto("paypal", "PayPal", null),
                PaymentMethod.YooKassa => new PaymentMethodDto("yookassa", "YooKassa", null),
                PaymentMethod.CorvusPay => new PaymentMethodDto("corvuspay", "CorvusPay", null),
                PaymentMethod.Xsolla => new PaymentMethodDto("xsolla", "Xsolla", null),
                PaymentMethod.MockProvider => new PaymentMethodDto("mockprovider", "Тестовая оплата (Sandbox)", "/images/providers/mock.svg"),
                _ => null
            })
            .Where(method => method is not null)
            .Cast<PaymentMethodDto>()
            .ToArray();
    }
}
