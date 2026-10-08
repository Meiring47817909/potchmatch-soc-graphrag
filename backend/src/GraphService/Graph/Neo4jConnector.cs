using Neo4j.Driver;

namespace GraphService.Graph;

public class Neo4jConnector(IDriver driver, ILogger<Neo4jConnector> log)
{
    /// <summary>Retries connectivity with exponential backoff (1s doubling, capped at 30s) until Neo4j answers.</summary>
    public async Task WaitUntilAvailableAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(30);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await driver.VerifyConnectivityAsync();
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Neo4j not available yet; retrying in {Delay}s", delay.TotalSeconds);
            }
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min(max.TotalSeconds, delay.TotalSeconds * 2));
        }
    }

    /// <summary>Creates one uniqueness constraint per label on its key property (idempotent).</summary>
    public async Task EnsureConstraintsAsync(CancellationToken ct)
    {
        await using var session = driver.AsyncSession();
        foreach (var (label, key) in GraphSchema.KeyProperty)
        {
            ct.ThrowIfCancellationRequested();
            var cypher = $"CREATE CONSTRAINT {label}_{key}_unique IF NOT EXISTS FOR (n:{label}) REQUIRE n.{key} IS UNIQUE";
            await session.RunAsync(cypher);
        }
        log.LogInformation("Ensured {Count} graph uniqueness constraints", GraphSchema.KeyProperty.Count);
    }
}
