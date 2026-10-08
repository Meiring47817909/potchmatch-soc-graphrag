using Neo4j.Driver;
using Testcontainers.Neo4j;

namespace GraphService.Tests;

public class Neo4jFixture : IAsyncLifetime
{
    private readonly Neo4jContainer _container = new Neo4jBuilder("neo4j:5-community").Build();

    public IDriver Driver { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Driver = GraphDatabase.Driver(
            // The module hands back a neo4j:// (routing) URI, which cannot route through a mapped port; use direct bolt://.
            _container.GetConnectionString().Replace("neo4j://", "bolt://"), AuthTokens.None);
    }

    public async Task ResetAsync()
    {
        await using var session = Driver.AsyncSession();
        await session.RunAsync("MATCH (n) DETACH DELETE n");
    }

    public async Task DisposeAsync()
    {
        if (Driver is not null) await Driver.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("neo4j")]
public class Neo4jCollection : ICollectionFixture<Neo4jFixture> { }
