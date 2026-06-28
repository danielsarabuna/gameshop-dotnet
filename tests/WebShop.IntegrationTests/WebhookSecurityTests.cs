using System.Security.Cryptography;
using System.Text;
using EventBus;
using Ordering.Application.Abstractions;
using Ordering.Application.Payments;
using Ordering.Application.PromoCodes;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;
using Ordering.Domain.Products;
using Ordering.Infrastructure.Payments;

namespace WebShop.IntegrationTests;

/// <summary>
/// Unit-level checks for webhook authenticity verification (Ф1):
/// signature schemes, YooKassa source allowlist, amount validation.
/// </summary>
public sealed class WebhookSecurityTests
{
    private const string Secret = "whsec_test_secret";

    // ---------- Xsolla: sha1(md5(body + secret)) ----------

    [Fact]
    public void Xsolla_signature_valid_header_is_accepted()
    {
        var body = """{"notification_type":"payment","external_id":"abc"}""";
        var header = "Signature " + ComputeXsollaSignature(body, Secret);

        Assert.True(XsollaPaymentProvider.VerifySignature(body, Secret, header));
    }

    [Fact]
    public void Xsolla_signature_tampered_body_is_rejected()
    {
        var body = """{"notification_type":"payment","external_id":"abc"}""";
        var header = "Signature " + ComputeXsollaSignature(body, Secret);
        var tampered = """{"notification_type":"payment","external_id":"other"}""";

        Assert.False(XsollaPaymentProvider.VerifySignature(tampered, Secret, header));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Signature deadbeef")]
    public void Xsolla_signature_bad_headers_are_rejected(string? header)
    {
        Assert.False(XsollaPaymentProvider.VerifySignature("{}", Secret, header));
    }

    private static string ComputeXsollaSignature(string body, string secret)
    {
        var md5Hex = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(body + secret)));
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(md5Hex)));
    }

    // ---------- YooKassa: source allowlist ----------

    [Theory]
    [InlineData("185.71.76.5", true)]      // inside documented /27
    [InlineData("185.71.76.31", true)]     // last host of /27
    [InlineData("185.71.76.32", false)]    // first address past /27
    [InlineData("77.75.156.11", true)]     // documented exact IP
    [InlineData("77.75.154.200", true)]    // inside second /25
    [InlineData("77.75.155.0", false)]     // outside /25
    [InlineData("1.2.3.4", false)]
    [InlineData(null, false)]
    [InlineData("not-an-ip", false)]
    public void Yookassa_allowlist_matches_documented_ranges(string? ip, bool expected)
    {
        Assert.Equal(expected, YooKassaWebhookSource.IsAllowed(ip));
    }

    [Fact]
    public void Yookassa_allowlist_honours_extra_rules()
    {
        Assert.True(YooKassaWebhookSource.IsAllowed("203.0.113.5", ["203.0.113.0/29"]));
        Assert.False(YooKassaWebhookSource.IsAllowed("203.0.113.20", ["203.0.113.0/29"]));
    }

    // ---------- Handler: webhook amount must match order total ----------

    [Fact]
    public async Task Handler_accepts_webhook_with_matching_amount()
    {
        var harness = new Harness(total: 9.99m, currency: "EUR");
        var result = await harness.Handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest("evt_ok", harness.OrderId, Guid.NewGuid(), "succeeded", Amount: 9.99m, Currency: "EUR"),
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(OrderStatus.Paid, harness.Order.Status);
    }

    [Fact]
    public async Task Handler_rejects_webhook_with_wrong_amount()
    {
        var harness = new Harness(total: 9.99m, currency: "EUR");

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest("evt_bad_amount", harness.OrderId, Guid.NewGuid(), "succeeded", Amount: 4.99m, Currency: "EUR"),
            CancellationToken.None));

        Assert.NotEqual(OrderStatus.Paid, harness.Order.Status);
    }

    [Fact]
    public async Task Handler_rejects_webhook_with_wrong_currency()
    {
        var harness = new Harness(total: 9.99m, currency: "EUR");

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest("evt_bad_ccy", harness.OrderId, Guid.NewGuid(), "succeeded", Amount: 9.99m, Currency: "USD"),
            CancellationToken.None));
    }

    /// <summary>In-memory wiring for handler tests; no stores are mutated on rejection paths.</summary>
    private sealed class Harness
    {
        public Guid OrderId { get; } = Guid.NewGuid();
        public Order Order { get; private set; }
        public HandleWebhookHandler Handler { get; }

        public Harness(decimal total, string currency)
        {
            Order = new Order(
                OrderId,
                "player-1",
                PaymentMethod.Stripe,
                [new OrderItem(Guid.NewGuid(), "Diamonds", ProductType.Currency, total, 1, new Dictionary<string, string>())],
                currency,
                0m,
                null,
                DateTimeOffset.UtcNow);

            Handler = new HandleWebhookHandler(
                new FakeOrderRepository(this),
                new FakePaymentStore(),
                new FakeWebhookIdempotencyStore(),
                new FakePromoCodeStore());
        }

        private sealed class FakeOrderRepository(Harness harness) : IOrderRepository
        {
            public Task AddAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken)
                => Task.FromResult(id == harness.OrderId ? harness.Order : null);

            public Task UpdateAsync(Order order, CancellationToken cancellationToken)
            {
                harness.Order = order;
                return Task.CompletedTask;
            }

            public Task UpdateWithOutboxAsync(Order order, IReadOnlyList<OutboxMessage> outbox, CancellationToken cancellationToken)
            {
                harness.Order = order;
                return Task.CompletedTask;
            }
        }
    }

    private sealed class FakePaymentStore : IPaymentStore
    {
        private Payment? _payment;

        public Task<Payment?> GetByOrderAsync(Guid orderId, PaymentMethod provider, CancellationToken cancellationToken)
            => Task.FromResult(_payment);

        public Task AddAsync(Payment payment, CancellationToken cancellationToken)
        {
            _payment = payment;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
        {
            _payment = payment;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWebhookIdempotencyStore : IWebhookIdempotencyStore
    {
        public bool TryBegin(string provider, string eventId) => true;
    }

    private sealed class FakePromoCodeStore : IPromoCodeStore
    {
        public Task<PromoCode?> GetAsync(string code, CancellationToken cancellationToken) => Task.FromResult<PromoCode?>(null);

        public Task<bool> TryConsumeAsync(string code, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
