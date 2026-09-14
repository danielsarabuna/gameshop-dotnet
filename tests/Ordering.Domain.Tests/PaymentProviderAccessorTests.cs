using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Ordering.Domain.Payments;
using Ordering.Infrastructure.Payments;

namespace Ordering.Domain.Tests;

public sealed class PaymentProviderAccessorTests
{
    [Theory]
    [InlineData("PayPal", PaymentMethod.PayPal)]
    [InlineData("CorvusPay", PaymentMethod.CorvusPay)]
    public void ExperimentalProvider_WithCredentialsOnly_RemainsUnavailable(
        string provider,
        PaymentMethod method)
    {
        var accessor = CreateAccessor(provider, enabled: false);

        Assert.Null(accessor.GetProvider(method));
        Assert.DoesNotContain(method, accessor.GetAvailableMethods());
    }

    [Theory]
    [InlineData("PayPal", PaymentMethod.PayPal)]
    [InlineData("CorvusPay", PaymentMethod.CorvusPay)]
    public void ExperimentalProvider_WithExplicitOptIn_BecomesAvailable(
        string provider,
        PaymentMethod method)
    {
        var accessor = CreateAccessor(provider, enabled: true);

        Assert.NotNull(accessor.GetProvider(method));
        Assert.Contains(method, accessor.GetAvailableMethods());
    }

    private static PaymentProviderAccessor CreateAccessor(string provider, bool enabled)
    {
        var values = new Dictionary<string, string?>
        {
            [$"Payments:{provider}:Enabled"] = enabled.ToString(),
            ["Payments:PayPal:ClientId"] = "portfolio-client",
            ["Payments:PayPal:ClientSecret"] = "portfolio-secret",
            ["Payments:PayPal:Mode"] = "sandbox",
            ["Payments:CorvusPay:StoreId"] = "portfolio-store",
            ["Payments:CorvusPay:SecretKey"] = "portfolio-secret",
            ["Payments:CorvusPay:ApiUrl"] = "https://example.invalid/payment"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new PaymentProviderAccessor(configuration, new TestHostEnvironment());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "WebShop.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
