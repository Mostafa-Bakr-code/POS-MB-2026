using POS_MB.Business;

namespace POS_MB.API;

// Runs for the lifetime of the API process, checking every minute for three
// different kinds of stuck mobile orders: ones stuck at Placed too long (the
// resilience safety net - clsOrderBusiness.CancelStaleMobileOrdersAsync),
// ones stuck at AwaitingPayment too long, i.e. an abandoned/never-finished
// Paymob checkout (clsOrderBusiness.CancelAbandonedPaymentsAsync), and a
// one-time follow-up on orders the second check already gave up on, in case
// the payment resolved shortly after (clsOrderBusiness.RecheckRecentlyAbandonedPaymentsAsync).
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

                var stalePlacedCount = await orderBusiness.CancelStaleMobileOrdersAsync();
                if (stalePlacedCount > 0)
                    logger.LogInformation("Auto-cancelled {Count} stale mobile order(s)", stalePlacedCount);

                var abandonedPaymentCount = await orderBusiness.CancelAbandonedPaymentsAsync();
                if (abandonedPaymentCount > 0)
                    logger.LogInformation("Auto-cancelled {Count} abandoned/unpaid mobile order(s)", abandonedPaymentCount);

                var recheckedCount = await orderBusiness.RecheckRecentlyAbandonedPaymentsAsync();
                if (recheckedCount > 0)
                    logger.LogInformation("Reconciled {Count} recently-abandoned order(s) that turned out to have actually been paid", recheckedCount);
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
