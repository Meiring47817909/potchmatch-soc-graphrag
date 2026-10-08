using Neo4j.Driver;
using GraphService.Incidents;
using GraphService.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GraphService.Tests;

[Collection("neo4j")]
public class IncidentWriterTests(Neo4jFixture fx)
{
    private const string Ip = "9.9.9.9";

    private async Task<SyncHarness> FreshAsync()
    {
        await fx.ResetAsync();
        await SyncHarness.LoadSeedsAsync(fx.Driver);
        return new SyncHarness(fx.Driver);
    }

    private static IncidentWriter NewWriter(IDriver d) =>
        new(d, Options.Create(new IncidentOptions()), NullLogger<IncidentWriter>.Instance);

    private static SyncAlert IpAlert(int n, string rule, int minutes, string status = "OPEN")
    {
        var ev = SyncHarness.FailedLogin(n * 10, null, null) with { SourceIp = Ip };
        return new SyncAlert(Guid.Parse($"bbbbbbbb-0000-0000-0000-{n:D12}"), SyncHarness.T0.AddMinutes(minutes),
            SyncHarness.T0.AddMinutes(minutes), rule, "High", status, "d", null, null, null, "act", [ev]);
    }

    [Fact]
    public async Task Correlate_FailedLoginsThenUnauthorisedSameIp_EscalatedIncident()
    {
        var h = await FreshAsync();
        await h.Writer.UpsertAlertAsync(IpAlert(1, "Multiple Failed Logins", 0), default);
        await h.Writer.UpsertAlertAsync(IpAlert(2, "Unauthorised Endpoint Access", 5), default);
        await NewWriter(fx.Driver).CorrelateAsync(default);

        Assert.Equal(1, await h.CountAsync("MATCH (i:Incident) RETURN count(i)"));
        Assert.Equal(2, await h.CountAsync("MATCH (:Incident)-[c:CONTAINS]->(:Alert) RETURN count(c)"));
        var row = (await h.QueryAsync("MATCH (i:Incident) RETURN i.escalated AS e, i.priority AS p, i.subject AS s, i.status AS st, i.title AS t"))[0];
        Assert.True(row["e"].As<bool>());
        Assert.Equal("HIGH", row["p"].As<string>());
        Assert.Equal("ip:" + Ip, row["s"].As<string>());
        Assert.Equal("OPEN", row["st"].As<string>());
        Assert.Equal("Activity from ip:9.9.9.9: Multiple Failed Logins, Unauthorised Endpoint Access", row["t"].As<string>());
    }

    [Fact]
    public async Task Correlate_Twice_NoDuplicates()
    {
        var h = await FreshAsync();
        await h.Writer.UpsertAlertAsync(IpAlert(1, "Multiple Failed Logins", 0), default);
        await h.Writer.UpsertAlertAsync(IpAlert(2, "Unauthorised Endpoint Access", 5), default);
        var w = NewWriter(fx.Driver);
        await w.CorrelateAsync(default);
        await w.CorrelateAsync(default);
        Assert.Equal(1, await h.CountAsync("MATCH (i:Incident) RETURN count(i)"));
        Assert.Equal(2, await h.CountAsync("MATCH (:Incident)-[c:CONTAINS]->(:Alert) RETURN count(c)"));
    }

    [Fact]
    public async Task Correlate_AllAlertsResolved_IncidentResolved()
    {
        var h = await FreshAsync();
        await h.Writer.UpsertAlertAsync(IpAlert(1, "Multiple Failed Logins", 0), default);
        var w = NewWriter(fx.Driver);
        await w.CorrelateAsync(default);
        await h.Writer.UpsertAlertAsync(IpAlert(1, "Multiple Failed Logins", 0, "RESOLVED"), default);
        await w.CorrelateAsync(default);
        Assert.Equal("RESOLVED", (await h.QueryAsync("MATCH (i:Incident) RETURN i.status"))[0][0].As<string>());
        // reopened alert flips it back
        await h.Writer.UpsertAlertAsync(IpAlert(1, "Multiple Failed Logins", 0, "OPEN"), default);
        await w.CorrelateAsync(default);
        Assert.Equal("OPEN", (await h.QueryAsync("MATCH (i:Incident) RETURN i.status"))[0][0].As<string>());
    }

    [Fact]
    public async Task Correlate_EmailAffectedUser_SubjectIsRealUserId()
    {
        var h = await FreshAsync();
        await h.Writer.UpsertPlayersAsync([SyncHarness.Player()], default);
        await h.Writer.UpsertAlertAsync(SyncHarness.Alert() with { AffectedUser = " Wickusvn@Gmail.com" }, default);
        await NewWriter(fx.Driver).CorrelateAsync(default);
        Assert.Equal("user:" + SyncHarness.PlayerId,
            (await h.QueryAsync("MATCH (i:Incident) RETURN i.subject"))[0][0].As<string>());
    }

    [Fact]
    public async Task Correlate_UnknownEmailAffectedUser_SubjectIsPlaceholder()
    {
        var h = await FreshAsync();
        await h.Writer.UpsertAlertAsync(SyncHarness.Alert(userId: "nobody@x.com") with { AffectedUser = "nobody@x.com" }, default);
        await NewWriter(fx.Driver).CorrelateAsync(default);
        Assert.Equal("user:email:nobody@x.com",
            (await h.QueryAsync("MATCH (i:Incident) RETURN i.subject"))[0][0].As<string>());
    }

    [Fact]
    public async Task Correlate_PlaceholderUserLaterRegisters_SingleEscalatedIncidentOnRealUser()
    {
        var h = await FreshAsync();
        const string email = "late@x.com";
        var realId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // Failed logins for an email that is not yet known to the graph -> placeholder subject.
        await h.Writer.UpsertAlertAsync(SyncHarness.Alert(userId: email) with { AffectedUser = email }, default);
        var w = NewWriter(fx.Driver);
        await w.CorrelateAsync(default);
        Assert.Equal("user:email:" + email, (await h.QueryAsync("MATCH (i:Incident) RETURN i.subject"))[0][0].As<string>());

        // Players sync now includes the email: placeholder merges into the real user.
        await h.Writer.UpsertPlayersAsync([SyncHarness.Player(email: email, id: realId)], default);

        // Unauthorised access 5 min later, identified by GUID.
        var ev = SyncHarness.FailedLogin(50, realId.ToString(), "/api/graph/incidents") with
        {
            EventType = "UNAUTHORISED_ACCESS", HttpMethod = "GET", StatusCode = 403, Timestamp = SyncHarness.T0.AddMinutes(5),
        };
        var unauthorised = new SyncAlert(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000050"), SyncHarness.T0.AddMinutes(5),
            SyncHarness.T0.AddMinutes(5), "Unauthorised Endpoint Access", "Medium", "OPEN", "d", "identity-service",
            realId.ToString(), null, "act", [ev]);
        await h.Writer.UpsertAlertAsync(unauthorised, default);
        await w.CorrelateAsync(default);

        var rows = await h.QueryAsync("MATCH (i:Incident) RETURN i.subject AS s, i.escalated AS e");
        var row = Assert.Single(rows);
        Assert.True(row["e"].As<bool>());
        Assert.Equal("user:" + realId, row["s"].As<string>());
        Assert.Equal(2, await h.CountAsync("MATCH (:Incident)-[c:CONTAINS]->(:Alert) RETURN count(c)"));
    }
}
