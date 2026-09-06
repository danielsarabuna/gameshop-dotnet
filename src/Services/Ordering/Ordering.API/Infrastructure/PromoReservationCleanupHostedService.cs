using Ordering.Application.Abstractions;

namespace Ordering.API.Infrastructure;

/// <summary>Expires abandoned checkouts so a finite-use promo cannot be held indefinitely.</summary>
public sealed class PromoReservationCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PromoReservationCleanupHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var released = await scope.ServiceProvider
                    .GetRequiredService<IPromoReservationMaintenanceStore>()
                    .ReleaseExpiredAsync(stoppingToken);
                if (released > 0)
                {
                    logger.LogInformation("Released {ReservationCount} expired promo reservations.", released);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Promo reservation cleanup tick failed.");
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
    }
}
