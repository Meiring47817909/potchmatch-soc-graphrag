using GraphService.Seeding;

namespace GraphService.Graph;

/// <summary>
/// Background startup: waits for Neo4j, ensures constraints, loads the seed files, then opens the gate. Does not block host startup.
/// Invalid seeds are terminal (gate failed); any other error is treated as transient and the sequence is retried with backoff.
/// </summary>
public class GraphStartupService(
    Neo4jConnector connector, SeedLoader seedLoader, GraphStartupGate gate, IConfiguration config, ILogger<GraphStartupService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(30);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await connector.WaitUntilAvailableAsync(stoppingToken);
                await connector.EnsureConstraintsAsync(stoppingToken);
                await LoadSeedsAsync(stoppingToken);
                gate.MarkReady();
                log.LogInformation("Graph startup complete");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (SeedValidationException ex)
            {
                log.LogError("Seed file {Path} is invalid ({Count} errors): {Errors}", ex.Path, ex.Errors.Count, string.Join(" | ", ex.Errors));
                gate.MarkFailed(ex);
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Graph startup failed; retrying in {Delay}s", delay.TotalSeconds);
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromSeconds(Math.Min(max.TotalSeconds, delay.TotalSeconds * 2));
        }
    }

    private async Task LoadSeedsAsync(CancellationToken ct)
    {
        var dir = config["GraphSeed:Directory"] ?? "/app/graph-data";
        IReadOnlySet<NodeRef> loaded = new HashSet<NodeRef>();
        foreach (var relative in new[] { "app-structure.json", Path.Combine("knowledge", "security-knowledge.json") })
        {
            var path = Path.Combine(dir, relative);
            if (!File.Exists(path))
            {
                log.LogWarning("Seed file {Path} not found; skipping", path);
                continue;
            }
            loaded = await seedLoader.LoadAsync(path, loaded, ct);
        }
    }
}
