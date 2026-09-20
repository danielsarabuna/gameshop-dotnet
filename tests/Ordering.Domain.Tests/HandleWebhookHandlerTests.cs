using EventBus;
using Ordering.Application.Abstractions;
using Ordering.Application.Payments;
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
            new FakePaymentWebhookStore(payments));

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
        var payments = new FakePaymentStore(null);
        var webhooks = new FakePaymentWebhookStore(payments);
        var handler = new HandleWebhookHandler(
            orders,
            payments,
            webhooks);

        var processed = await handler.HandleAsync(
            PaymentMethod.YooKassa,
            new PaymentWebhookRequest("evt_outbox_1", orderId, Guid.NewGuid(), "succeeded"),
            CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(2, webhooks.LastCommand!.Outbox.Count);
        Assert.Contains(webhooks.LastCommand.Outbox, m => m.Type == "order_completed.v1");
        Assert.Contains(webhooks.LastCommand.Outbox, m => m.Type == "supabase.order_paid.v1");
    }

    [Fact]
    public async Task Transient_commit_failure_allows_provider_retry()
    {
        var orderId = Guid.NewGuid();
        var order = new Order(
            orderId,
            "player-1",
            PaymentMethod.Stripe,
            [new OrderItem(Guid.NewGuid(), "Diamonds", ProductType.Currency, 9.99m, 1, new Dictionary<string, string>())],
            "EUR",
            0m,
            null,
            DateTimeOffset.UtcNow);

        var orders = new FakeOrderRepository(order);
        var payments = new FakePaymentStore(null);
        var webhooks = new FakePaymentWebhookStore(payments, failuresBeforeSuccess: 1);
        var handler = new HandleWebhookHandler(
            orders,
            payments,
            webhooks);

        // First attempt: repository fails transiently AFTER the slot was reserved.
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest("evt_transient", orderId, Guid.NewGuid(), "succeeded"),
            CancellationToken.None));
        // Retry (provider redelivers): the transactional store now succeeds.
        var processed = await handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest("evt_transient", orderId, Guid.NewGuid(), "succeeded"),
            CancellationToken.None);
        Assert.True(processed);
    }

    [Fact]
    public async Task Business_rejection_does_not_start_transactional_commit()
    {
        var orderId = Guid.NewGuid();
        var order = new Order(
            orderId,
            "player-1",
            PaymentMethod.Stripe,
            [new OrderItem(Guid.NewGuid(), "Diamonds", ProductType.Currency, 9.99m, 1, new Dictionary<string, string>())],
            "EUR",
            0m,
            null,
            DateTimeOffset.UtcNow);

        var payments = new FakePaymentStore(null);
        var webhooks = new FakePaymentWebhookStore(payments);
        var handler = new HandleWebhookHandler(
            new FakeOrderRepository(order),
            payments,
            webhooks);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.HandleAsync(
            PaymentMethod.Stripe,
            new PaymentWebhookRequest("evt_mismatch", orderId, Guid.NewGuid(), "succeeded", Amount: 1.00m, Currency: "EUR"),
            CancellationToken.None));

        Assert.Null(webhooks.LastCommand);
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

        public Task<PaymentReservation> GetOrAddAsync(Payment payment, CancellationToken cancellationToken)
        {
            var created = _payment is null;
            _payment ??= payment;
            return Task.FromResult(new PaymentReservation(_payment, created));
        }

        public Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
        {
            _payment = payment;
            UpdatedPayment = payment;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentWebhookStore(
        FakePaymentStore payments,
        int failuresBeforeSuccess = 0) : IPaymentWebhookStore
    {
        private int _remainingFailures = failuresBeforeSuccess;
        public PaymentWebhookCommit? LastCommand { get; private set; }

        public async Task<PaymentWebhookCommitResult> CommitAsync(PaymentWebhookCommit command, CancellationToken cancellationToken)
        {
            if (_remainingFailures > 0)
            {
                Interlocked.Decrement(ref _remainingFailures);
                throw new InvalidOperationException("db momentarily unavailable");
            }

            LastCommand = command;
            await payments.UpdateAsync(command.Payment, cancellationToken);
            return PaymentWebhookCommitResult.Applied;
        }
    }
}
