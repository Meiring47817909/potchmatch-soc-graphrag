using GraphService.Graph;
using GraphService.Incidents;
using Microsoft.Extensions.Options;

namespace GraphService.Sync;

/// <summary>Pulls players, alerts and events from the other services and writes them to Neo4j.</summary>
public class SyncWorker(
    ISocSyncClient soc,
    IIdentitySyncClient identity,
    GraphWriter writer,
    GraphStartupGate gate,
    IIncidentCorrelation correlation,
    IOptions<GraphSyncOptions> options,
    ILogger<SyncWorker> log) : BackgroundService
{
    private readonly GraphSyncOptions _opt = options.Value;
    private DateTimeOffset? _lastPlayers;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await gate.WhenReady.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Graph startup failed; sync worker will not run");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Sync run failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_opt.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        await SyncPlayersAsync(now, ct);
        await SyncAlertsAsync(ct);
        await SyncEventsAsync(ct);
        try
        {
            await correlation.CorrelateAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Incident correlation failed");
        }
    }

    private async Task SyncPlayersAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (_lastPlayers is not null && now - _lastPlayers < TimeSpan.FromSeconds(_opt.PlayersIntervalSeconds)) return;
        try
        {
            var players = await identity.GetPlayersAsync(ct);
            await writer.UpsertPlayersAsync(players, ct);
            _lastPlayers = now;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Player sync failed");
        }
    }

    private async Task SyncAlertsAsync(CancellationToken ct)
    {
        try
        {
            var cursor = await writer.GetCursorAsync("alerts", ct);
            while (true)
            {
                var page = await soc.GetAlertsAsync(cursor, _opt.AlertPageSize, ct);
                foreach (var alert in page)
                {
                    await writer.UpsertAlertAsync(alert, ct);
                    cursor = new SyncCursor(alert.UpdatedAt, alert.AlertId);
                    await writer.SetCursorAsync("alerts", cursor, ct);
                }
                if (page.Count < _opt.AlertPageSize) break;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Alert sync failed");
        }
    }

    private async Task SyncEventsAsync(CancellationToken ct)
    {
        try
        {
            var cursor = await writer.GetCursorAsync("events", ct);
            while (true)
            {
                var page = await soc.GetEventsAsync(cursor, _opt.EventPageSize, ct);
                if (page.Count > 0)
                {
                    await writer.UpsertEventsAsync(page.Select(p => p.Event).ToList(), ct);
                    var last = page[^1];
                    cursor = new SyncCursor(last.ReceivedAt, last.Event.EventId);
                    await writer.SetCursorAsync("events", cursor, ct);
                }
                if (page.Count < _opt.EventPageSize) break;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Event sync failed");
        }
    }
}
