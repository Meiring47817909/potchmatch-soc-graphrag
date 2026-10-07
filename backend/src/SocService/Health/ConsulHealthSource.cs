using Consul;

namespace SocService.Health;

public class ConsulHealthSource(IConsulClient client) : IConsulHealthSource
{
    public const string Passing = "passing";
    public const string Warning = "warning";
    public const string Critical = "critical";

    public async Task<IReadOnlyDictionary<string, string>> GetStatusesAsync(CancellationToken ct)
    {
        var result = await client.Health.State(HealthStatus.Any, ct);
        return Aggregate(result.Response
            .Where(c => !string.IsNullOrEmpty(c.ServiceName)) // node-level checks (serfHealth) have no service name
            .Select(c => (c.ServiceName, c.Status.Status)));
    }

    /// <summary>
    /// Collapses per-instance checks to one status per service name. BEST status wins:
    /// a stale critical registration left by a killed instance must not mask a healthy one.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Aggregate(IEnumerable<(string ServiceName, string Status)> checks)
    {
        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, raw) in checks)
        {
            var status = raw.ToLowerInvariant() switch
            {
                "passing" => Passing,
                "warning" => Warning,
                _ => Critical,
            };
            statuses[name] = statuses.TryGetValue(name, out var existing) ? Best(existing, status) : status;
        }
        return statuses;
    }

    private static int Rank(string s) => s switch { Critical => 2, Warning => 1, _ => 0 };
    private static string Best(string a, string b) => Rank(a) <= Rank(b) ? a : b;
}
