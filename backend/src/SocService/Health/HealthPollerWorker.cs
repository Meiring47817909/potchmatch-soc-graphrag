using SocService.Data;

namespace SocService.Health;

public class HealthPollerWorker(
    IServiceScopeFactory scopeFactory,
    IConsulHealthSource source,
    ILogger<HealthPollerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private IReadOnlyDictionary<string, string>? _previous;

    public async Task PollOnceAsync(CancellationToken ct)
    {
        IReadOnlyDictionary<string, string> current;
        try
        {
            current = await source.GetStatusesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Consul health poll failed; keeping last known state");
            return;
        }

        var events = HealthTransitionDetector.Detect(_previous, current, DateTimeOffset.UtcNow);
        if (events.Count > 0)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SocDbContext>();
            db.Events.AddRange(events);
            await db.SaveChangesAsync(ct);
            foreach (var e in events)
                logger.LogInformation("Health transition: {EventType} for {Service}", e.EventType, e.AffectedEntity);
        }

        _previous = HealthTransitionDetector.NextBaseline(_previous, current);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Health poll cycle failed");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
