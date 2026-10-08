using GraphService.Graph;
using GraphService.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraphService.Tests;

[Collection("neo4j")]
public class GraphStartupServiceTests(Neo4jFixture fx)
{
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public int StopCalls { get; private set; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => StopCalls++;
    }

    [Fact]
    public async Task InvalidSeed_StopsApplicationWithExitCode1_AndFailsGate()
    {
        await fx.ResetAsync();
        var dir = Directory.CreateTempSubdirectory("bad-seed-");
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "app-structure.json"),
            """{"nodes":[{"label":"Banana","key":"x","properties":{}}],"relationships":[]}""");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GraphSeed:Directory"] = dir.FullName }).Build();
        var gate = new GraphStartupGate();
        var lifetime = new FakeLifetime();
        var svc = new GraphStartupService(
            new Neo4jConnector(fx.Driver, NullLogger<Neo4jConnector>.Instance),
            new SeedLoader(fx.Driver, NullLogger<SeedLoader>.Instance),
            gate, config, lifetime, NullLogger<GraphStartupService>.Instance);

        var previousExitCode = Environment.ExitCode;
        try
        {
            await svc.StartAsync(default);
            await svc.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(60));

            Assert.Equal(1, lifetime.StopCalls);
            Assert.Equal(1, Environment.ExitCode);
            await Assert.ThrowsAsync<SeedValidationException>(() => gate.WhenReady);
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
            dir.Delete(true);
        }
    }
}
