using IntegrationEvents;
using MassTransit;

namespace Ordering.API.Consumers;

public sealed class OrderCompletedLogConsumer : IConsumer<OrderCompleted>
{
    private readonly ILogger<OrderCompletedLogConsumer> _logger;

    public OrderCompletedLogConsumer(ILogger<OrderCompletedLogConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderCompleted> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "OrderCompleted received: OrderId={OrderId}, User={User}, Total={Total} {Currency}, PaymentId={PaymentId}",
            message.OrderId,
            message.GameUserId,
            message.Total,
            message.Currency,
            message.PaymentId);
        return Task.CompletedTask;
    }
}
