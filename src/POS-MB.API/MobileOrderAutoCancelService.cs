using POS_MB.Business;

namespace POS_MB.API;

// Runs for the lifetime of the API process, checking every minute for mobile
// orders that have sat at Placed too long (see
// clsOrderBusiness.CancelStaleMobileOrdersAsync) - the safety net that
// catches whatever slips past the manual "Accepting Online Orders" toggle
// and the heartbeat/offline check, regardless of the reason.
public class MobileOrderAutoCancelService(IServiceScopeFactory scopeFactory, ILogger<MobileOrderAutoCancelService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // BackgroundService itself is a singleton, but clsOrderBusiness
                // (and everything it depends on) is scoped - a fresh scope per
                // check keeps this consistent with how every request-driven
                // call already uses these services.
                using var scope = scopeFactory.CreateScope();
                var orderBusiness = scope.ServiceProvider.GetRequiredService<clsOrderBusiness>();

                var cancelledCount = await orderBusiness.CancelStaleMobileOrdersAsync();
                if (cancelledCount > 0)
                    logger.LogInformation("Auto-cancelled {Count} stale mobile order(s)", cancelledCount);
            }
            catch (Exception ex)
            {
                // A single failed check (e.g. a transient DB hiccup) must not
                // kill the whole background loop - just log it and try again
                // on the next tick.
                logger.LogError(ex, "Error while checking for stale mobile orders");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }
}
