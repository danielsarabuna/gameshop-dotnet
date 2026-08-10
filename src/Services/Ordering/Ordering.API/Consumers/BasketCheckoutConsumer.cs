using IntegrationEvents;
using MassTransit;
using Ordering.Application.Orders.CreateOrder;

namespace Ordering.API.Consumers;

public sealed class BasketCheckoutConsumer : IConsumer<BasketCheckout>
{
    private readonly CreateOrderHandler _createOrderHandler;
    private readonly ILogger<BasketCheckoutConsumer> _logger;

    public BasketCheckoutConsumer(CreateOrderHandler createOrderHandler, ILogger<BasketCheckoutConsumer> logger)
    {
        _createOrderHandler = createOrderHandler;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BasketCheckout> context)
    {
        var message = context.Message;
        _logger.LogInformation("Processing BasketCheckout event for User={UserId}, ItemsCount={Count}", message.UserId, message.Items.Count);

        var request = new CreateOrderRequest(
            GameUserId: message.UserId,
            PaymentMethod: Ordering.Domain.Payments.PaymentMethod.Stripe,
            Items: message.Items.Select(i => new CreateOrderLine(i.ProductId, i.Quantity)).ToList(),
            PromoCode: null
        );

        try
        {
            var result = await _createOrderHandler.HandleAsync(request, context.CancellationToken);
            _logger.LogInformation("Order created successfully via BasketCheckout: OrderId={OrderId}", result.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order from BasketCheckout event for User={UserId}", message.UserId);
            throw;
        }
    }
}
