namespace SocService.Health;

public interface IConsulHealthSource
{
    /// <summary>Service name to "passing" / "warning" / "critical" (worst check wins).</summary>
    Task<IReadOnlyDictionary<string, string>> GetStatusesAsync(CancellationToken ct);
}
