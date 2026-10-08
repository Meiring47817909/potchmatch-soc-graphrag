namespace GraphService.Graph;

/// <summary>Background startup: waits for Neo4j, ensures constraints, then opens the gate. Does not block host startup.</summary>
public class GraphStartupService(Neo4jConnector connector, GraphStartupGate gate, ILogger<GraphStartupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await connector.WaitUntilAvailableAsync(stoppingToken);
            await connector.EnsureConstraintsAsync(stoppingToken);
            gate.MarkReady();
            log.LogInformation("Graph startup complete");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Graph startup failed");
            gate.MarkFailed(ex);
        }
    }
}
