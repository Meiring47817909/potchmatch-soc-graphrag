using GraphService.Sync;
using Neo4j.Driver;

namespace GraphService.Tests;

[Collection("neo4j")]
public class GraphWriterTests(Neo4jFixture fx)
{
    private async Task<SyncHarness> FreshAsync()
    {
        await fx.ResetAsync();
        await SyncHarness.LoadSeedsAsync(fx.Driver);
        return new SyncHarness(fx.Driver);
    }

    [Fact]
    public async Task Cursor_RoundTrips_AndMissingIsNull()
    {
        var h = await FreshAsync();
        Assert.Null(await h.Writer.GetCursorAsync("alerts", default));
        var c = new SyncCursor(SyncHarness.T0.AddTicks(1234), Guid.NewGuid());
        await h.Writer.SetCursorAsync("alerts", c, default);
        await h.Writer.SetCursorAsync("alerts", c, default);
        Assert.Equal(c, await h.Writer.GetCursorAsync("alerts", default));
        Assert.Equal(1, await h.CountAsync("MATCH (s:SyncState) RETURN count(s)"));
    }

    [Fact]
    public async Task UpsertEvents_NonEmailUserAndNoEndpoint_NoTargetedAndNoDiscoveredService()
    {
        var h = await FreshAsync();
        var uid = Guid.NewGuid();
        var e = SyncHarness.FailedLogin(1, uid.ToString(), null) with { EventType = "HEALTH", ServiceName = "brand-new-svc" };
        await h.Writer.UpsertEventsAsync([e], default);
        Assert.Equal(0, await h.CountAsync("MATCH ()-[r:TARGETED]->() RETURN count(r)"));
        Assert.Equal(1, await h.CountAsync("MATCH (:User {userId: '" + uid + "'})-[:TRIGGERED]->(:Event) RETURN count(*)"));
        Assert.Equal(1, await h.CountAsync("MATCH (:Event)-[:GENERATED_BY]->(s:Service {name:'brand-new-svc', discovered:true}) RETURN count(s)"));
    }
}
