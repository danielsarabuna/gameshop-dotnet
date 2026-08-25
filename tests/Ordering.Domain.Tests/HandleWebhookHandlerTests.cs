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
            new FakePromoCodeStore());

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

    [Fact]
    public async Task Successful_webhook_enqueues_outbox_messages_atomically_with_order_update()
    {
        var orderId = Guid.NewGuid();
        var order = new Order(
            orderId,
            "player-1",
            PaymentMethod.YooKassa,
            [new OrderItem(Guid.NewGuid(), "60 Diamonds", ProductType.Currency, 4.99m, 1, new Dictionary<string, string> { ["diamonds"] = "60" })],
            "EUR",
            0m,
            null,
            DateTimeOffset.UtcNow);

        var orders = new FakeOrderRepository(order);
        var handler = new HandleWebhookHandler(
            orders,
            new FakePaymentStore(null),
            new FakeWebhookIdempotencyStore(),
            new FakePromoCodeStore());

        var processed = await handler.HandleAsync(
            PaymentMethod.YooKassa,
            new PaymentWebhookRequest("evt_outbox_1", orderId, Guid.NewGuid(), "succeeded"),
            CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(2, orders.Outbox.Count);
        Assert.Contains(orders.Outbox, m => m.Type == "order_completed.v1");
        Assert.Contains(orders.Outbox, m => m.Type == "supabase.order_paid.v1");
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private readonly Order _order;

        public FakeOrderRepository(Order order)
        {
            _order = order;
        }

        public List<OutboxMessage> Outbox { get; } = [];

        public Task AddAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken)
        {
            return Task.FromResult(id == _order.Id ? _order : null);
        }

        public Task UpdateAsync(Order order, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateWithOutboxAsync(Order order, IReadOnlyList<OutboxMessage> outbox, CancellationToken cancellationToken)
        {
            Outbox.AddRange(outbox);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentStore : IPaymentStore
    {
        private Payment? _payment;

        public FakePaymentStore(Payment? payment)
        {
            _payment = payment;
        }

        public Payment? UpdatedPayment { get; private set; }

        public Task<Payment?> GetByOrderAsync(Guid orderId, PaymentMethod provider, CancellationToken cancellationToken)
        {
            return Task.FromResult(_payment is not null && _payment.OrderId == orderId && _payment.Provider == provider
                ? _payment
                : null);
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
        public Task<PromoCode?> GetAsync(string code, CancellationToken cancellationToken) => Task.FromResult<PromoCode?>(null);

        public Task<bool> TryConsumeAsync(string code, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
