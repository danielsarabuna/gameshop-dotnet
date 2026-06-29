using EventBus;
using Ordering.Application.Abstractions;
using Ordering.Application.Payments;
using Ordering.Application.PromoCodes;
using Ordering.Domain.Orders;
using Ordering.Domain.Payments;
using Ordering.Domain.Products;

namespace Ordering.Domain.Tests;

public sealed class HandleWebhookHandlerTests
{
    [Fact]
    public async Task Existing_payment_is_updated_even_if_webhook_payment_id_differs()
    {
        var orderId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var existingPaymentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var webhookPaymentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var order = new Order(
            orderId,
            "player-1",
            PaymentMethod.Stripe,
            [new OrderItem(Guid.NewGuid(), "Diamonds", ProductType.Currency, 9.99m, 1, new Dictionary<string, string>())],
            "EUR",
            0m,
            null,
            DateTimeOffset.UtcNow);

        var payment = new Payment(
            existingPaymentId,
            orderId,
            PaymentMethod.Stripe,
            PaymentStatus.Pending,
            "pi_existing",
            DateTimeOffset.UtcNow,
            null);

        var orders = new FakeOrderRepository(order);
        var payments = new FakePaymentStore(payment);
        var handler = new HandleWebhookHandler(
            orders,
            payments,
            new FakeWebhookIdempotencyStore(),
            new FakePromoCodeStore(),
            new FakePurchaseRecorder(),
            NullEventBus.Instance);

        var processed = await handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest("evt_1", orderId, webhookPaymentId, "succeeded"),
            CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(existingPaymentId.ToString("D"), order.PaymentId);
        Assert.NotNull(payments.UpdatedPayment);
        Assert.Equal(existingPaymentId, payments.UpdatedPayment!.Id);
        Assert.Equal(PaymentStatus.Succeeded, payments.UpdatedPayment.Status);
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private readonly Order _order;

        public FakeOrderRepository(Order order)
        {
            _order = order;
        }

        public Task AddAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(id == _order.Id ? _order : null);
        }

        public Task UpdateAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePaymentStore : IPaymentStore
    {
        private Payment _payment;

        public FakePaymentStore(Payment payment)
        {
            _payment = payment;
        }

        public Payment? UpdatedPayment { get; private set; }

        public Task<Payment?> GetByOrderAsync(Guid orderId, PaymentMethod provider, CancellationToken cancellationToken)
        {
            return Task.FromResult(_payment.OrderId == orderId && _payment.Provider == provider ? _payment : null);
        }

        public Task AddAsync(Payment payment, CancellationToken cancellationToken)
        {
            _payment = payment;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
        {
            _payment = payment;
            UpdatedPayment = payment;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWebhookIdempotencyStore : IWebhookIdempotencyStore
    {
        public bool TryBegin(string provider, string eventId) => true;
    }

    private sealed class FakePromoCodeStore : IPromoCodeStore
    {
        public PromoCode? Get(string code) => null;
        public bool TryConsume(string code) => true;
    }

    private sealed class FakePurchaseRecorder : IPurchaseRecorder
    {
        public Task RecordAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
