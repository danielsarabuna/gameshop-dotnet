using System.Text.Json;
using EventBus;
using IntegrationEvents;
using Logging;
using Ordering.Application.Abstractions;
using Ordering.Infrastructure.Integrations;
using Ordering.Infrastructure.Persistence;

namespace Ordering.API.Infrastructure;

/// <summary>
/// Drains the outbox queue: publishes OrderCompleted to the event bus and delivers paid orders
/// to Supabase. At-least-once semantics — all handlers must be idempotent (they are).
/// </summary>
public sealed class OutboxDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatcherHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox dispatch tick failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Outbox dispatcher stopped.");
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxDispatcherStore>();

        var messages = await store.ClaimBatchAsync(BatchSize, cancellationToken);
        foreach (var message in messages)
        {
            try
            {
                await ProcessAsync(scope.ServiceProvider, message, cancellationToken);
                await store.AckAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (message.Attempts >= OutboxBackoff.PoisonThreshold)
                {
                    logger.LogError(ex,
                        "Outbox message {MessageId} ({MessageType}) failed {Threshold} times and was dead-lettered.",
                        message.Id, message.Type, OutboxBackoff.PoisonThreshold);
                    await store.DeadLetterAsync(message, ex.Message, cancellationToken);
                    continue;
                }

                logger.LogWarning(ex, "Outbox message {MessageId} ({MessageType}) failed (attempt {Attempt}).",
                    message.Id, message.Type, message.Attempts);
                await store.RetryAsync(message, OutboxBackoff.Next(message.Attempts), ex.Message, cancellationToken);
            }
        }
    }

    private static async Task ProcessAsync(IServiceProvider services, OutboxMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case OutboxMessageTypes.OrderCompleted:
                {
                    var completed = JsonSerializer.Deserialize<OrderCompleted>(message.PayloadJson, PayloadOptions)
                                    ?? throw new InvalidOperationException("Unreadable OrderCompleted payload.");
                    await services.GetRequiredService<IEventBus>().PublishAsync(completed, cancellationToken);
                    break;
                }
            case OutboxMessageTypes.SupabaseOrderPaid:
                {
                    var delivery = JsonSerializer.Deserialize<SupabaseOrderDelivery>(message.PayloadJson, PayloadOptions)
                                   ?? throw new InvalidOperationException("Unreadable SupabaseOrderDelivery payload.");
                    await services.GetRequiredService<ISupabaseOrderDelivery>().DeliverAsync(delivery, cancellationToken);
                    break;
                }
            default:
                throw new InvalidOperationException($"Unknown outbox message type '{message.Type}'.");
        }
    }
}
