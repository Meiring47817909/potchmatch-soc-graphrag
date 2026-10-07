using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SocService.Data;
using SocService.Health;

namespace SocService.Tests;

public class HealthPollerWorkerTests
{
    private class FakeSource : IConsulHealthSource
    {
        public IReadOnlyDictionary<string, string> Next { get; set; } = new Dictionary<string, string>();
        public bool Throw { get; set; }
        public Task<IReadOnlyDictionary<string, string>> GetStatusesAsync(CancellationToken ct) =>
            Throw ? throw new HttpRequestException("consul down") : Task.FromResult(Next);
    }

    private static (HealthPollerWorker Worker, FakeSource Source, ServiceProvider Sp) Build()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = new ServiceCollection()
            .AddDbContext<SocDbContext>(o => o.UseInMemoryDatabase(dbName))
            .BuildServiceProvider();
        var source = new FakeSource();
        return (new HealthPollerWorker(sp.GetRequiredService<IServiceScopeFactory>(), source,
            NullLogger<HealthPollerWorker>.Instance), source, sp);
    }

    private static int Count(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SocDbContext>().Events.Count();
    }

    [Fact]
    public async Task Poller_ConsulUnreachable_StoresNoEvents()
    {
        var (worker, source, sp) = Build();
        source.Next = new Dictionary<string, string> { ["payment-service"] = "passing" };
        await worker.PollOnceAsync(CancellationToken.None);   // baseline

        source.Throw = true;
        await worker.PollOnceAsync(CancellationToken.None);   // unreachable
        Assert.Equal(0, Count(sp));

        source.Throw = false;                                  // unchanged status
        await worker.PollOnceAsync(CancellationToken.None);
        Assert.Equal(0, Count(sp));
    }

    [Fact]
    public async Task Poller_Transitions_StoreEvents()
    {
        var (worker, source, sp) = Build();
        source.Next = new Dictionary<string, string> { ["payment-service"] = "passing" };
        await worker.PollOnceAsync(CancellationToken.None);
        source.Next = new Dictionary<string, string> { ["payment-service"] = "critical" };
        await worker.PollOnceAsync(CancellationToken.None);
        Assert.Equal(1, Count(sp));
        source.Next = new Dictionary<string, string> { ["payment-service"] = "passing" };
        await worker.PollOnceAsync(CancellationToken.None);
        Assert.Equal(2, Count(sp));
    }
}
