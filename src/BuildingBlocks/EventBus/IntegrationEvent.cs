namespace EventBus;

public abstract record IntegrationEvent(Guid Id, DateTimeOffset OccurredAtUtc);

