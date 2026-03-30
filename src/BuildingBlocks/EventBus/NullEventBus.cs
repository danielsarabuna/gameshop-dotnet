namespace EventBus;

public sealed class NullEventBus : IEventBus
{
    public static NullEventBus Instance { get; } = new();

    private NullEventBus()
    {
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        return Task.CompletedTask;
    }
}

