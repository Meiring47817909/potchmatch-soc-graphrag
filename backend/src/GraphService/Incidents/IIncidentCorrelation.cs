namespace GraphService.Incidents;

/// <summary>Correlates alerts into incidents; called by the sync worker at the end of each run.</summary>
public interface IIncidentCorrelation
{
    Task CorrelateAsync(CancellationToken ct);
}

/// <summary>Placeholder until the real incident writer is registered.</summary>
public class NoOpIncidentCorrelation : IIncidentCorrelation
{
    public Task CorrelateAsync(CancellationToken ct) => Task.CompletedTask;
}
