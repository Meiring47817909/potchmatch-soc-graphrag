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
        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var check in result.Response)
        {
            // Node-level checks (serfHealth) have no service name.
            if (string.IsNullOrEmpty(check.ServiceName))
                continue;

            var status = check.Status.Status.ToLowerInvariant() switch
            {
                "passing" => Passing,
                "warning" => Warning,
                _ => Critical,
            };
            statuses[check.ServiceName] = statuses.TryGetValue(check.ServiceName, out var existing)
                ? Worst(existing, status)
                : status;
        }
        return statuses;
    }

    private static int Rank(string s) => s switch { Critical => 2, Warning => 1, _ => 0 };
    private static string Worst(string a, string b) => Rank(a) >= Rank(b) ? a : b;
}
