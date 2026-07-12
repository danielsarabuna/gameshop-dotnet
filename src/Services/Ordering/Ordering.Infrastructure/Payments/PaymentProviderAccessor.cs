using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Ordering.Application.Payments;
using Ordering.Domain.Payments;
using DomainPaymentMethod = Ordering.Domain.Payments.PaymentMethod;

namespace Ordering.Infrastructure.Payments;

public class PaymentProviderAccessor : IPaymentProviderAccessor
{
    private readonly Dictionary<DomainPaymentMethod, IPaymentProvider> _providers;

    public PaymentProviderAccessor(IConfiguration configuration, IHostEnvironment environment)
    {
        _providers = new Dictionary<DomainPaymentMethod, IPaymentProvider>();

        var mockSetting = configuration["Payments:MockProvider:Enabled"];
        bool isMockEnabled = false;
        if (!string.IsNullOrEmpty(mockSetting))
        {
            bool.TryParse(mockSetting, out isMockEnabled);
        }
        else if (environment != null)
        {
            isMockEnabled = environment.IsDevelopment();
        }

        if (isMockEnabled)
        {
            _providers[DomainPaymentMethod.MockProvider] = new MockPaymentProvider();
        }

        var stripeKey = configuration["Payments:Stripe:SecretKey"];
        var stripeWebhookSecret = configuration["Payments:Stripe:WebhookSecret"];
        if (!string.IsNullOrEmpty(stripeKey) && stripeKey != "test-payment-placeholder")
        {
            _providers[DomainPaymentMethod.Stripe] = new StripePaymentProvider(
                stripeKey,
                stripeWebhookSecret,
                configuration["Payments:Stripe:SuccessUrl"],
                configuration["Payments:Stripe:CancelUrl"]);
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
        var yooKassaTrustedIps = configuration["Payments:YooKassa:TrustedIps"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!string.IsNullOrEmpty(shopId) &&
            !string.IsNullOrEmpty(secretKey) &&
            shopId != "placeholder" &&
            secretKey != "placeholder")
        {
            _providers[DomainPaymentMethod.YooKassa] = new YooKassaPaymentProvider(
                shopId, secretKey, yooKassaTrustedIps, configuration["Payments:YooKassa:ReturnUrl"]);
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
        if (!string.IsNullOrEmpty(xsollaMerchantId) &&
            !string.IsNullOrEmpty(xsollaApiKey) &&
            !string.IsNullOrEmpty(xsollaProjectId) &&
            xsollaMerchantId != "placeholder" &&
            xsollaApiKey != "placeholder" &&
            xsollaProjectId != "placeholder")
        {
            _providers[DomainPaymentMethod.Xsolla] = new XsollaPaymentProvider(
                xsollaMerchantId,
                xsollaApiKey,
                xsollaProjectId,
                xsollaMode,
                xsollaWebhookSecret,
                configuration["Payments:Xsolla:ReturnUrl"]);
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
