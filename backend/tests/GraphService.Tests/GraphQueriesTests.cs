using GraphService.Controllers;
using GraphService.Incidents;
using GraphService.Queries;
using GraphService.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace GraphService.Tests;

[Collection("neo4j")]
public class GraphQueriesTests(Neo4jFixture fx)
{
    private static readonly Guid AlertTwoId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private async Task<SyncHarness> BuildAsync()
    {
        await fx.ResetAsync();
        await SyncHarness.LoadSeedsAsync(fx.Driver);
        var h = new SyncHarness(fx.Driver);
        await h.Writer.UpsertPlayersAsync([SyncHarness.Player()], default);
        var one = SyncHarness.Alert() with { Severity = "HIGH" };
        var two = SyncHarness.Alert(rule: "Unauthorised Endpoint Access") with
        {
            AlertId = AlertTwoId,
            Severity = "MEDIUM",
            RelatedEvents = [SyncHarness.FailedLogin(6), SyncHarness.FailedLogin(7)],
        };
        await h.Writer.UpsertAlertAsync(one, default);
        await h.Writer.UpsertAlertAsync(two, default);
        await new IncidentWriter(fx.Driver, Options.Create(new IncidentOptions()), NullLogger<IncidentWriter>.Instance)
            .CorrelateAsync(default);
        return h;
    }

    private GraphQueryRunner Runner() => new(fx.Driver);

    private static Dictionary<string, object?> P(string key, object? value) => new() { [key] = value };

    [Fact]
    public async Task AlertsAffectingService_IdentityService_ReturnsAlert()
    {
        await BuildAsync();
        var r = await Runner().RunAsync("alerts-affecting-service", GraphQueries.AlertsAffectingService, P("name", "identity-service"), default);
        Assert.Equal(2, r.Rows.Count);
        Assert.Contains(r.Rows, row => (string)row["alertId"]! == "aaaaaaaa-0000-0000-0000-000000000001" && (string)row["ruleName"]! == "Multiple Failed Logins");
        Assert.Contains(r.Nodes, n => n.Label == "Service" && (string)n.Properties["name"]! == "identity-service");
        Assert.Contains(r.Relationships, x => x.Type == "AFFECTS");
        Assert.IsType<string>(r.Rows[0]["timestamp"]); // temporal converted to ISO string
    }

    [Fact]
    public async Task UsersHighSeverity_ReturnsWickusWithCount()
    {
        await BuildAsync();
        var r = await Runner().RunAsync("users-high-severity", GraphQueries.UsersHighSeverity, new Dictionary<string, object?>(), default);
        var row = Assert.Single(r.Rows);
        Assert.Equal(SyncHarness.PlayerId.ToString(), (string)row["userId"]!);
        Assert.Equal("Wickus Meiring", (string)row["name"]!);
        Assert.Equal(1L, row["alertCount"]); // only the HIGH alert counts
        Assert.Contains(r.Relationships, x => x.Type == "CREATED_ALERT");
        Assert.Contains(r.Relationships, x => x.Type == "TRIGGERED");
    }

    [Fact]
    public async Task ThreatsForService_IdentityService_IncludesT1110AndT1078()
    {
        await BuildAsync();
        var r = await Runner().RunAsync("threats-for-service", GraphQueries.ThreatsForService, P("name", "identity-service"), default);
        var ids = r.Rows.Select(x => (string)x["threatId"]!).ToHashSet();
        Assert.Contains("T1110", ids);
        Assert.Contains("T1078", ids);
    }

    [Fact]
    public async Task ControlsForAlert_FailedLogins_ReturnsT1110Controls()
    {
        await BuildAsync();
        var r = await Runner().RunAsync("controls-for-alert", GraphQueries.ControlsForAlert,
            P("alertId", "aaaaaaaa-0000-0000-0000-000000000001"), default);
        var controls = r.Rows.Select(x => (string)x["controlId"]!).ToHashSet();
        Assert.Contains("M1036", controls);
        Assert.Contains("M1032", controls);
        Assert.Contains("M1027", controls);
        Assert.All(r.Rows, x => Assert.Equal("T1110", (string)x["threatId"]!));
        Assert.Contains(r.Nodes, n => n.Label == "Document" && (string)n.Properties["id"]! == "DOC-PASSWORD-RESET");
        Assert.Contains(r.Relationships, x => x.Type == "MITIGATES");
        Assert.Contains(r.Relationships, x => x.Type == "SUPPORTS_RESPONSE_TO");
        Assert.Contains(r.Relationships, x => x.Type == "DESCRIBES");
        Assert.Contains(r.Nodes, n => n.Label == "Vulnerability");
    }

    [Fact]
    public async Task DependencyImpact_PaymentService_IncludesBookingAndGateway()
    {
        await BuildAsync();
        var r = await Runner().RunAsync("dependency-impact", GraphQueries.DependencyImpact, P("name", "payment-service"), default);
        Assert.Contains(r.Rows, x => (string)x["service"]! == "booking-service" && (long)x["depth"]! == 1);
        Assert.Contains(r.Rows, x => (string)x["service"]! == "api-gateway" && (long)x["depth"]! == 1);
        // One row per dependent service, at its shortest depth (api-gateway also reaches payment via booking-service).
        Assert.Equal(r.Rows.Count, r.Rows.Select(x => (string)x["service"]!).Distinct().Count());
        // Evidence still includes the longer path through booking-service.
        Assert.Contains(r.Relationships, x => x.Type == "DEPENDS_ON"
            && r.Nodes.Any(n => n.Id == x.Start && (string?)n.Properties.GetValueOrDefault("name") == "api-gateway")
            && r.Nodes.Any(n => n.Id == x.End && (string?)n.Properties.GetValueOrDefault("name") == "booking-service"));
        Assert.All(r.Relationships, x => Assert.Equal("DEPENDS_ON", x.Type));
    }

    [Fact]
    public async Task Incidents_ListAndDetail()
    {
        await BuildAsync();
        var list = await Runner().RunAsync("incidents", GraphQueries.Incidents, P("status", null), default);
        var inc = Assert.Single(list.Rows);
        Assert.True((bool)inc["escalated"]!);
        Assert.Equal("HIGH", (string)inc["priority"]!);
        Assert.Equal("OPEN", (string)inc["status"]!);
        // Distinct rule names of the contained alerts, sorted.
        Assert.Equal(new[] { "Multiple Failed Logins", "Unauthorised Endpoint Access" },
            ((System.Collections.IEnumerable)inc["rules"]!).Cast<object?>().Select(r => (string)r!).ToArray());

        var resolved = await Runner().RunAsync("incidents", GraphQueries.Incidents, P("status", "RESOLVED"), default);
        Assert.Empty(resolved.Rows);

        var id = (string)inc["incidentId"]!;
        var detail = await Runner().RunAsync("incident-detail", GraphQueries.IncidentDetail, P("incidentId", id), default);
        Assert.Equal(2, detail.Rows.Count);
        Assert.Contains(detail.Nodes, n => n.Label == "Incident");
        Assert.Contains(detail.Nodes, n => n.Label == "User");
        Assert.Contains(detail.Nodes, n => n.Label == "Threat" && (string)n.Properties["id"]! == "T1078");
        Assert.Contains(detail.Relationships, x => x.Type == "CONTAINS");
    }

    [Fact]
    public async Task Result_NodesAndRelationshipsDeduplicated()
    {
        await BuildAsync();
        var r = await Runner().RunAsync("controls-for-alert", GraphQueries.ControlsForAlert,
            P("alertId", "aaaaaaaa-0000-0000-0000-000000000001"), default);
        Assert.True(r.Rows.Count >= 3);
        Assert.Equal(r.Nodes.Count, r.Nodes.Select(n => n.Id).Distinct().Count());
        Assert.Equal(r.Relationships.Count, r.Relationships.Select(x => x.Id).Distinct().Count());
        // the single INDICATES edge and Alert node are shared by every row
        Assert.Single(r.Relationships, x => x.Type == "INDICATES");
        Assert.Single(r.Nodes, n => n.Label == "Alert");
        // every relationship endpoint is a returned node
        var ids = r.Nodes.Select(n => n.Id).ToHashSet();
        Assert.All(r.Relationships, x => { Assert.Contains(x.Start, ids); Assert.Contains(x.End, ids); });
    }

    [Fact]
    public async Task Result_IncludesCypherAndParameters()
    {
        await BuildAsync();
        var p = P("name", "payment-service");
        var r = await Runner().RunAsync("dependency-impact", GraphQueries.DependencyImpact, p, default);
        Assert.Equal("dependency-impact", r.Query);
        Assert.Equal(GraphQueries.DependencyImpact, r.Cypher);
        Assert.Equal("payment-service", r.Parameters["name"]);
        Assert.Single(r.Parameters);
    }

    [Fact]
    public async Task Controller_UnknownEntities_Return404()
    {
        await BuildAsync();
        var c = new GraphController(Runner());
        Assert.IsType<NotFoundResult>(await c.ServiceAlerts("nope", default));
        Assert.IsType<NotFoundResult>(await c.ServiceThreats("nope", default));
        Assert.IsType<NotFoundResult>(await c.ServiceImpact("nope", default));
        Assert.IsType<NotFoundResult>(await c.AlertControls("00000000-0000-0000-0000-00000000dead", default));
        Assert.IsType<NotFoundResult>(await c.IncidentDetail("INC-nope", default));
        Assert.IsType<OkObjectResult>(await c.ServiceImpact("payment-service", default));
        Assert.IsType<BadRequestObjectResult>(await c.Incidents("bogus", default));
    }

    [Fact]
    public async Task Controller_NeoUnavailable_Returns503()
    {
        await using var dead = GraphDatabase.Driver("bolt://127.0.0.1:1", AuthTokens.None,
            o => o.WithMaxTransactionRetryTime(TimeSpan.FromMilliseconds(200)));
        var c = new GraphController(new GraphQueryRunner(dead));
        var res = await c.UsersHighSeverity(default);
        Assert.Equal(503, Assert.IsType<ObjectResult>(res).StatusCode);
    }
}
