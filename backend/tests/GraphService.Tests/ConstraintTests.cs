using Neo4j.Driver;
using GraphService.Graph;
using Microsoft.Extensions.Logging.Abstractions;

namespace GraphService.Tests;

[Collection("neo4j")]
public class ConstraintTests(Neo4jFixture fx)
{
    [Fact]
    public async Task EnsureConstraints_CreatesOnePerLabel_Idempotent()
    {
        var connector = new Neo4jConnector(fx.Driver, NullLogger<Neo4jConnector>.Instance);
        await connector.WaitUntilAvailableAsync(CancellationToken.None);

        await connector.EnsureConstraintsAsync(CancellationToken.None);
        await connector.EnsureConstraintsAsync(CancellationToken.None);

        await using var session = fx.Driver.AsyncSession();
        var cursor = await session.RunAsync("SHOW CONSTRAINTS YIELD name RETURN name");
        var names = (await cursor.ToListAsync()).Select(r => r["name"].As<string>()).ToList();

        Assert.Equal(13, names.Count);
        foreach (var (label, key) in GraphSchema.KeyProperty)
            Assert.Contains($"{label}_{key}_unique", names);
    }
}
