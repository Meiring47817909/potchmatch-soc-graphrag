namespace GraphService.Graph;

/// <summary>Signals when the graph is ready (Neo4j reachable, schema ensured, seed loaded). Workers await <see cref="WhenReady"/>.</summary>
public class GraphStartupGate
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WhenReady => _tcs.Task;

    public void MarkReady() => _tcs.TrySetResult();

    public void MarkFailed(Exception ex) => _tcs.TrySetException(ex);
}
