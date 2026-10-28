using GraphService.Sync;
using Neo4j.Driver;

namespace GraphService.Tests;

[Collection("neo4j")]
public class SyncWorkerTests(Neo4jFixture fx)
{
    private async Task<SyncHarness> FreshAsync()
    {
        await fx.ResetAsync();
        await SyncHarness.LoadSeedsAsync(fx.Driver);
        return new SyncHarness(fx.Driver);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sync_AlertWithEvents_CreatesFullChain()
    {
        var h = await FreshAsync();
        h.Identity.Players.Add(SyncHarness.Player());
        h.Soc.Alerts.Add(SyncHarness.Alert());

        await h.Worker.RunOnceAsync(Now, default);

        Assert.Equal(5, await h.CountAsync(
            "MATCH (u:User {userId:'11111111-1111-1111-1111-111111111111'})-[:TRIGGERED]->(e:Event)-[:CREATED_ALERT]->(a:Alert)-[:INDICATES]->(:Threat {id:'T1110'}) RETURN count(e)"));
        Assert.Equal(5, await h.CountAsync("MATCH (:Event)-[:GENERATED_BY]->(:Service {name:'identity-service'}) RETURN count(*)"));
        Assert.Equal(5, await h.CountAsync("MATCH (:Event)-[:TARGETED]->(:Endpoint {key:'POST /api/auth/login'}) RETURN count(*)"));
        Assert.Equal(1, await h.CountAsync("MATCH (:Alert)-[:AFFECTS]->(:Service {name:'identity-service'}) RETURN count(*)"));
        Assert.Equal(1, await h.CountAsync("MATCH (u:User)-[:HAS_ROLE]->(:Role {name:'Player'}) RETURN count(u)"));
        Assert.Equal(1, h.Correlation.Calls);
        // datetime, not string
        var rec = (await h.QueryAsync("MATCH (a:Alert) RETURN a.timestamp AS t, a.updatedAt AS u"))[0];
        Assert.IsType<ZonedDateTime>(rec["t"]);
        Assert.IsType<ZonedDateTime>(rec["u"]);
        Assert.NotNull(await h.Writer.GetCursorAsync("alerts", default));
    }

    [Fact]
    public async Task Sync_SameAlertTwice_IsIdempotent()
    {
        var h = await FreshAsync();
        h.Identity.Players.Add(SyncHarness.Player());
        h.Soc.Alerts.Add(SyncHarness.Alert());
        await h.Worker.RunOnceAsync(Now, default);
        var before = await h.QueryAsync("MATCH ()-[r]->() RETURN type(r) AS k, count(r) AS c");

        h.Soc.Alerts.Clear();
        h.Soc.Alerts.Add(SyncHarness.Alert("RESOLVED", 6, updatedAt: SyncHarness.T0.AddMinutes(5)));
        await h.Worker.RunOnceAsync(Now.AddSeconds(15), default);

        Assert.Equal(1, await h.CountAsync("MATCH (a:Alert {status:'RESOLVED'}) RETURN count(a)"));
        Assert.Equal(1, await h.CountAsync("MATCH (a:Alert) RETURN count(a)"));
        Assert.Equal(6, await h.CountAsync("MATCH (e:Event) RETURN count(e)"));
        var after = (await h.QueryAsync("MATCH ()-[r]->() RETURN type(r) AS k, count(r) AS c"))
            .ToDictionary(r => r["k"].As<string>(), r => r["c"].As<long>());
        foreach (var r in before)
        {
            var k = r["k"].As<string>();
            var expected = r["c"].As<long>() + (k is "TRIGGERED" or "GENERATED_BY" or "TARGETED" or "CREATED_ALERT" ? 1 : 0);
            Assert.Equal(expected, after[k]);
        }
    }

    [Fact]
    public async Task Sync_FailedLoginEmailCase_LinksToExistingUser()
    {
        var h = await FreshAsync();
        h.Identity.Players.Add(SyncHarness.Player());
        h.Soc.Events.Add(new SyncEventEnvelope(SyncHarness.FailedLogin(1, " Wickus.Coach@Potchmatch.Demo"), SyncHarness.T0));
        await h.Worker.RunOnceAsync(Now, default);

        Assert.Equal(1, await h.CountAsync("MATCH (:User {userId:'11111111-1111-1111-1111-111111111111'})-[:TRIGGERED]->(:Event) RETURN count(*)"));
        Assert.Equal(0, await h.CountAsync("MATCH (u:User {unknownAccount:true}) RETURN count(u)"));
    }

    [Fact]
    public async Task Sync_UnknownEmail_CreatesUnknownUser_ThenMergesWhenRegistered()
    {
        var h = await FreshAsync();
        h.Soc.Events.Add(new SyncEventEnvelope(SyncHarness.FailedLogin(1, "Ghost@Example.com"), SyncHarness.T0));
        await h.Worker.RunOnceAsync(Now, default);
        Assert.Equal(1, await h.CountAsync("MATCH (:User {userId:'email:ghost@example.com', email:'ghost@example.com', unknownAccount:true})-[:TRIGGERED]->(:Event) RETURN count(*)"));

        var id = Guid.NewGuid();
        h.Identity.Players.Add(SyncHarness.Player(email: "ghost@example.com", id: id));
        await h.Worker.RunOnceAsync(Now.AddMinutes(2), default);

        Assert.Equal(0, await h.CountAsync("MATCH (u:User {unknownAccount:true}) RETURN count(u)"));
        Assert.Equal(1, await h.CountAsync($"MATCH (:User {{userId:'{id}'}})-[:TRIGGERED]->(:Event) RETURN count(*)"));
        Assert.Equal(1, await h.CountAsync("MATCH (e:Event) RETURN count(e)"));
    }

    [Fact]
    public async Task Sync_RoleChange_ReplacesHasRole()
    {
        var h = await FreshAsync();
        h.Identity.Players.Add(SyncHarness.Player("Player"));
        await h.Worker.RunOnceAsync(Now, default);
        h.Identity.Players.Clear();
        h.Identity.Players.Add(SyncHarness.Player("Coach"));
        await h.Worker.RunOnceAsync(Now.AddMinutes(2), default);

        var roles = await h.QueryAsync("MATCH (:User)-[:HAS_ROLE]->(r:Role) RETURN r.name AS n");
        Assert.Single(roles);
        Assert.Equal("Coach", roles[0]["n"].As<string>());
    }

    [Fact]
    public async Task Sync_NoSyncState_FetchesFromStart()
    {
        var h = await FreshAsync();
        await h.Worker.RunOnceAsync(Now, default);
        Assert.Null(Assert.Single(h.Soc.AlertCalls));
        Assert.Null(Assert.Single(h.Soc.EventCalls));
    }

    [Fact]
    public async Task Sync_ClientThrows_CursorUnchanged()
    {
        var h = await FreshAsync();
        h.Soc.Alerts.Add(SyncHarness.Alert());
        await h.Worker.RunOnceAsync(Now, default);
        var cursor = await h.Writer.GetCursorAsync("alerts", default);
        Assert.NotNull(cursor);

        h.Soc.Throw = true;
        h.Identity.Throw = true;
        await h.Worker.RunOnceAsync(Now.AddMinutes(2), default);

        Assert.Equal(cursor, await h.Writer.GetCursorAsync("alerts", default));
        Assert.Equal(1, await h.CountAsync("MATCH (a:Alert) RETURN count(a)"));
    }

    [Fact]
    public async Task Sync_UnmappedRule_LoadsWithoutIndicates()
    {
        var h = await FreshAsync();
        h.Soc.Alerts.Add(SyncHarness.Alert(rule: "Brand New Rule"));
        await h.Worker.RunOnceAsync(Now, default);
        Assert.Equal(1, await h.CountAsync("MATCH (a:Alert) RETURN count(a)"));
        Assert.Equal(0, await h.CountAsync("MATCH ()-[r:INDICATES]->() RETURN count(r)"));
    }

    [Fact]
    public async Task Sync_UnknownEndpoint_CreatesDiscovered()
    {
        var h = await FreshAsync();
        h.Soc.Events.Add(new SyncEventEnvelope(
            SyncHarness.FailedLogin(1, null, "/api/mystery/5e9a3b2c-0000-0000-0000-000000000001?x=1"), SyncHarness.T0));
        await h.Worker.RunOnceAsync(Now, default);
        Assert.Equal(1, await h.CountAsync(
            "MATCH (:Event)-[:TARGETED]->(e:Endpoint {key:'POST /api/mystery/{id}', discovered:true}) RETURN count(e)"));
    }

    [Fact]
    public async Task Sync_IdentityTimesOut_AlertsEventsAndCorrelationStillRun()
    {
        var h = await FreshAsync();
        h.Identity.Timeout = true;
        h.Soc.Alerts.Add(SyncHarness.Alert());
        h.Soc.Events.Add(new SyncEventEnvelope(SyncHarness.FailedLogin(9, null), SyncHarness.T0));

        await h.Worker.RunOnceAsync(Now, default);

        Assert.Equal(1, await h.CountAsync("MATCH (a:Alert) RETURN count(a)"));
        Assert.Equal(6, await h.CountAsync("MATCH (e:Event) RETURN count(e)"));
        Assert.Equal(1, h.Correlation.Calls);
    }

    [Fact]
    public async Task Sync_RoleNodeMissing_KeepsExistingHasRole()
    {
        var h = await FreshAsync();
        h.Identity.Players.Add(SyncHarness.Player("Player"));
        await h.Worker.RunOnceAsync(Now, default);
        h.Identity.Players.Clear();
        h.Identity.Players.Add(SyncHarness.Player("Referee"));
        await h.Worker.RunOnceAsync(Now.AddMinutes(2), default);

        var roles = await h.QueryAsync("MATCH (:User)-[:HAS_ROLE]->(r:Role) RETURN r.name AS n");
        Assert.Equal("Player", Assert.Single(roles)["n"].As<string>());
    }

    [Fact]
    public async Task Sync_PlaceholderCreatedBetweenPlayerSyncs_RunsPlayersPhaseBeforeCorrelation()
    {
        var h = await FreshAsync();
        await h.Worker.RunOnceAsync(Now, default); // players synced now (none registered yet)

        var id = Guid.NewGuid();
        h.Identity.Players.Add(SyncHarness.Player(email: "ghost@example.com", id: id));
        h.Soc.Events.Add(new SyncEventEnvelope(SyncHarness.FailedLogin(1, "ghost@example.com"), SyncHarness.T0));
        await h.Worker.RunOnceAsync(Now.AddSeconds(15), default); // regular players interval not yet due

        Assert.Equal(0, await h.CountAsync("MATCH (u:User {unknownAccount:true}) RETURN count(u)"));
        Assert.Equal(1, await h.CountAsync($"MATCH (:User {{userId:'{id}'}})-[:TRIGGERED]->(:Event) RETURN count(*)"));
        Assert.Equal(2, h.Correlation.Calls);
    }

    [Fact]
    public async Task Sync_PlaceholderCatchUpFails_OtherPhasesStillRun()
    {
        var h = await FreshAsync();
        await h.Worker.RunOnceAsync(Now, default);

        h.Identity.Throw = true;
        h.Soc.Events.Add(new SyncEventEnvelope(SyncHarness.FailedLogin(1, "ghost@example.com"), SyncHarness.T0));
        h.Soc.Alerts.Add(SyncHarness.Alert(userId: "ghost@example.com"));
        await h.Worker.RunOnceAsync(Now.AddSeconds(15), default);

        Assert.Equal(1, await h.CountAsync("MATCH (u:User {unknownAccount:true}) RETURN count(u)"));
        Assert.Equal(1, await h.CountAsync("MATCH (a:Alert) RETURN count(a)"));
        Assert.Equal(2, h.Correlation.Calls);
    }
}
