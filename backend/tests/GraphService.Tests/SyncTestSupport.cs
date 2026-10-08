using GraphService.Graph;
using GraphService.Incidents;
using GraphService.Seeding;
using GraphService.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace GraphService.Tests;

internal sealed class FakeSocClient : ISocSyncClient
{
    public List<SyncAlert> Alerts { get; } = [];
    public List<SyncEventEnvelope> Events { get; } = [];
    public bool Throw { get; set; }
    public List<SyncCursor?> AlertCalls { get; } = [];
    public List<SyncCursor?> EventCalls { get; } = [];

    public Task<IReadOnlyList<SyncAlert>> GetAlertsAsync(SyncCursor? after, int limit, CancellationToken ct)
    {
        AlertCalls.Add(after);
        if (Throw) throw new HttpRequestException("boom");
        IReadOnlyList<SyncAlert> r = Alerts
            .Where(a => after is null || a.UpdatedAt > after.At || (a.UpdatedAt == after.At && a.AlertId.CompareTo(after.Id) > 0))
            .OrderBy(a => a.UpdatedAt).ThenBy(a => a.AlertId).Take(limit).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<SyncEventEnvelope>> GetEventsAsync(SyncCursor? after, int limit, CancellationToken ct)
    {
        EventCalls.Add(after);
        if (Throw) throw new HttpRequestException("boom");
        IReadOnlyList<SyncEventEnvelope> r = Events
            .Where(e => after is null || e.ReceivedAt > after.At || (e.ReceivedAt == after.At && e.Event.EventId.CompareTo(after.Id) > 0))
            .OrderBy(e => e.ReceivedAt).ThenBy(e => e.Event.EventId).Take(limit).ToList();
        return Task.FromResult(r);
    }
}

internal sealed class FakeIdentityClient : IIdentitySyncClient
{
    public List<SyncPlayer> Players { get; } = [];
    public bool Throw { get; set; }

    public Task<IReadOnlyList<SyncPlayer>> GetPlayersAsync(CancellationToken ct)
    {
        if (Throw) throw new HttpRequestException("boom");
        return Task.FromResult<IReadOnlyList<SyncPlayer>>(Players.ToList());
    }
}

internal sealed class CountingCorrelation : IIncidentCorrelation
{
    public int Calls { get; private set; }
    public Task CorrelateAsync(CancellationToken ct) { Calls++; return Task.CompletedTask; }
}

internal sealed class SyncHarness
{
    public FakeSocClient Soc { get; } = new();
    public FakeIdentityClient Identity { get; } = new();
    public CountingCorrelation Correlation { get; } = new();
    public GraphWriter Writer { get; }
    public SyncWorker Worker { get; }
    public IDriver Driver { get; }

    public SyncHarness(IDriver driver)
    {
        Driver = driver;
        Writer = new GraphWriter(driver, NullLogger<GraphWriter>.Instance);
        var gate = new GraphStartupGate();
        gate.MarkReady();
        Worker = new SyncWorker(Soc, Identity, Writer, gate, Correlation,
            Options.Create(new GraphSyncOptions()), NullLogger<SyncWorker>.Instance);
    }

    public static string SeedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "graph-data"))) dir = dir.Parent;
        return dir is null ? throw new DirectoryNotFoundException("graph-data not found") : Path.Combine(dir.FullName, "graph-data");
    }

    public static async Task LoadSeedsAsync(IDriver driver)
    {
        var loader = new SeedLoader(driver, NullLogger<SeedLoader>.Instance);
        var dir = SeedDirectory();
        var structure = await loader.LoadAsync(Path.Combine(dir, "app-structure.json"), new HashSet<NodeRef>(), default);
        await loader.LoadAsync(Path.Combine(dir, "knowledge", "security-knowledge.json"), structure, default);
    }

    public async Task<List<IRecord>> QueryAsync(string cypher, object? parameters = null)
    {
        await using var session = Driver.AsyncSession();
        var cursor = await session.RunAsync(cypher, parameters);
        return await cursor.ToListAsync();
    }

    public async Task<long> CountAsync(string cypher) =>
        (await QueryAsync(cypher))[0][0].As<long>();

    public static readonly Guid PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const string Email = "wickusvn@gmail.com";
    public static readonly DateTimeOffset T0 = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    public static SyncPlayer Player(string role = "Player", string email = Email, Guid? id = null) =>
        new(id ?? PlayerId, email, "Wickus", "Meiring", role);

    public static SyncEvent FailedLogin(int n, string? userId = " Wickusvn@Gmail.com", string? endpoint = "/api/auth/login",
        string service = "identity-service") =>
        new(Guid.Parse($"00000000-0000-0000-0000-{n:D12}"), T0.AddSeconds(n), service, "FAILED_LOGIN", "Warning",
            userId, "10.0.0.5", endpoint, "POST", 401, "Failed login", "corr-" + n, null);

    public static SyncAlert Alert(string status = "OPEN", int events = 5, string rule = "Multiple Failed Logins",
        string? userId = " Wickusvn@Gmail.com", string? endpoint = "/api/auth/login", DateTimeOffset? updatedAt = null)
    {
        var evs = Enumerable.Range(1, events).Select(i => FailedLogin(i, userId, endpoint)).ToList();
        return new SyncAlert(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), T0, updatedAt ?? T0.AddMinutes(1), rule, "High", status,
            "5 failed logins", "identity-service", null, null, "Lock the account", evs);
    }
}
