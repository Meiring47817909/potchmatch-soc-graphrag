using Microsoft.Extensions.Options;

namespace SocService.Detection;

public class DetectionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DetectionOptions> options,
    ILogger<DetectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.EngineIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<DetectionEngine>();
                await engine.RunOnceAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Detection cycle failed");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
