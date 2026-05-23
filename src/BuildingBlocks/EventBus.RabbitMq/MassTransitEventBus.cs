using MassTransit;

namespace EventBus.RabbitMq;

internal sealed class MassTransitEventBus : IEventBus
{
    private readonly IBus _bus;

    public MassTransitEventBus(IBus bus)
    {
        _bus = bus;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        return _bus.Publish(@event, @event.GetType(), cancellationToken);
    }
}
