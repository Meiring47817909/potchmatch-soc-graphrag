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
        var playersSynced = await SyncPlayersAsync(now, force: false, ct);
        await SyncAlertsAsync(ct);
        await SyncEventsAsync(ct);
        await CatchUpPlaceholderUsersAsync(now, playersSynced, ct);
        try
        {
            await correlation.CorrelateAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Incident correlation failed");
        }
    }

    /// <summary>
    /// A placeholder user (failed logins for an email not yet seen in Identity) may belong to someone who registered
    /// since the last players sync. Sync players now, before correlation, so incidents get the real user as subject.
    /// </summary>
    private async Task CatchUpPlaceholderUsersAsync(DateTimeOffset now, bool playersSynced, CancellationToken ct)
    {
        if (playersSynced) return;
        try
        {
            if (!await writer.HasPlaceholderUsersAsync(ct)) return;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Placeholder user check failed");
            return;
        }
        await SyncPlayersAsync(now, force: true, ct);
    }

    /// <summary>Returns true when players were attempted in this call (whether or not the fetch succeeded).</summary>
    private async Task<bool> SyncPlayersAsync(DateTimeOffset now, bool force, CancellationToken ct)
    {
        if (!force && _lastPlayers is not null && now - _lastPlayers < TimeSpan.FromSeconds(_opt.PlayersIntervalSeconds)) return false;
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
        return true;
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
