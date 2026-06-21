using Microsoft.Extensions.Configuration;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;

namespace Ordering.Infrastructure.Payments;

public class PaymentProviderAccessor : IPaymentProviderAccessor
{
    private readonly Dictionary<DomainPaymentMethod, IPaymentProvider> _providers;

    public PaymentProviderAccessor(IConfiguration configuration)
    {
        _providers = new Dictionary<DomainPaymentMethod, IPaymentProvider>();

        var stripeKey = configuration["Payments:Stripe:SecretKey"];
        var stripeWebhookSecret = configuration["Payments:Stripe:WebhookSecret"];
        if (!string.IsNullOrEmpty(stripeKey) && stripeKey != "test-payment-placeholder")
        {
            _providers[DomainPaymentMethod.Stripe] = new StripePaymentProvider(stripeKey, stripeWebhookSecret);
        }

        var payPalClientId = configuration["Payments:PayPal:ClientId"];
        var payPalClientSecret = configuration["Payments:PayPal:ClientSecret"];
        var payPalMode = configuration["Payments:PayPal:Mode"] ?? "sandbox";
        var payPalWebhookSecret = configuration["Payments:PayPal:WebhookSecret"];
        if (!string.IsNullOrEmpty(payPalClientId) &&
            !string.IsNullOrEmpty(payPalClientSecret) &&
            payPalClientId != "placeholder" &&
            payPalClientSecret != "placeholder")
        {
            _providers[DomainPaymentMethod.PayPal] = new PayPalPaymentProvider(payPalClientId, payPalClientSecret, payPalMode, payPalWebhookSecret);
        }

        var shopId = configuration["Payments:YooKassa:ShopId"];
        var secretKey = configuration["Payments:YooKassa:SecretKey"];
        var yooKassaWebhookSecret = configuration["Payments:YooKassa:WebhookSecret"];
        if (!string.IsNullOrEmpty(shopId) &&
            !string.IsNullOrEmpty(secretKey) &&
            shopId != "placeholder" &&
            secretKey != "placeholder")
        {
            _providers[DomainPaymentMethod.YooKassa] = new YooKassaPaymentProvider(shopId, secretKey, yooKassaWebhookSecret);
        }

        var corvusPayStoreId = configuration["Payments:CorvusPay:StoreId"];
        var corvusPaySecret = configuration["Payments:CorvusPay:SecretKey"];
        var corvusPayApiUrl = configuration["Payments:CorvusPay:ApiUrl"];
        var corvusPayWebhookSecret = configuration["Payments:CorvusPay:WebhookSecret"];
        if (!string.IsNullOrEmpty(corvusPayStoreId) && corvusPayStoreId != "placeholder")
        {
            _providers[DomainPaymentMethod.CorvusPay] = new CorvusPayPaymentProvider(
                corvusPayStoreId, 
                corvusPaySecret ?? "placeholder", 
                corvusPayApiUrl ?? "https://corvuspay.com/payment",
                corvusPayWebhookSecret);
        }

        var xsollaMerchantId = configuration["Payments:Xsolla:MerchantId"];
        var xsollaApiKey = configuration["Payments:Xsolla:ApiKey"];
        var xsollaProjectId = configuration["Payments:Xsolla:ProjectId"];
        var xsollaMode = configuration["Payments:Xsolla:Mode"] ?? "sandbox";
        var xsollaWebhookSecret = configuration["Payments:Xsolla:WebhookSecret"];
        if (!string.IsNullOrEmpty(xsollaMerchantId) && !string.IsNullOrEmpty(xsollaApiKey) && !string.IsNullOrEmpty(xsollaProjectId))
        {
            _providers[DomainPaymentMethod.Xsolla] = new XsollaPaymentProvider(
                xsollaMerchantId,
                xsollaApiKey,
                xsollaProjectId,
                xsollaMode,
                xsollaWebhookSecret);
        }
    }

    public IPaymentProvider? GetProvider(DomainPaymentMethod method)
    {
        return _providers.TryGetValue(method, out var provider) ? provider : null;
    }

    public IReadOnlyCollection<DomainPaymentMethod> GetAvailableMethods()
    {
        return _providers.Keys.ToArray();
    }
}
