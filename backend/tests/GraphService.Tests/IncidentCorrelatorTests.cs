using GraphService.Incidents;

namespace GraphService.Tests;

public class IncidentCorrelatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    private static AlertForCorrelation A(string id, int minutes, string subject = "user:u1", string rule = "Multiple Failed Logins") =>
        new(id, T0.AddMinutes(minutes), rule, "High", "OPEN", subject);

    [Fact]
    public void SameSubjectWithinWindow_Joins()
    {
        var r = IncidentCorrelator.Assign([A("a1", 0), A("a2", 10)], [], Window);
        Assert.Equal(2, r.Count);
        Assert.Equal(r[0].IncidentId, r[1].IncidentId);
        Assert.Equal(IncidentIds.For("user:u1", "a1"), r[0].IncidentId);
    }

    [Fact]
    public void SameSubjectOutsideWindow_NewIncident()
    {
        var r = IncidentCorrelator.Assign([A("a1", 0), A("a2", 31)], [], Window);
        Assert.NotEqual(r[0].IncidentId, r[1].IncidentId);
    }

    [Fact]
    public void ExactlyAtWindow_Joins()
    {
        var r = IncidentCorrelator.Assign([A("a1", 0), A("a2", 30)], [], Window);
        Assert.Equal(r[0].IncidentId, r[1].IncidentId);
    }

    [Fact]
    public void ChainedAlerts_ExtendLastAlertAt()
    {
        var r = IncidentCorrelator.Assign([A("a1", 0), A("a2", 25), A("a3", 50)], [], Window);
        Assert.Single(r.Select(x => x.IncidentId).Distinct());
    }

    [Fact]
    public void ResolvedIncident_NotReused()
    {
        var existing = new OpenIncident("INC-old", "user:u1", T0, "RESOLVED");
        var r = IncidentCorrelator.Assign([A("a1", 5)], [existing], Window);
        Assert.NotEqual("INC-old", r[0].IncidentId);
    }

    [Fact]
    public void OpenExistingIncident_Joined()
    {
        var existing = new OpenIncident("INC-old", "user:u1", T0, "OPEN");
        var r = IncidentCorrelator.Assign([A("a1", 5)], [existing], Window);
        Assert.Equal("INC-old", r[0].IncidentId);
    }

    [Fact]
    public void DifferentSubjects_Separate()
    {
        var r = IncidentCorrelator.Assign([A("a1", 0, "user:u1"), A("a2", 1, "ip:1.2.3.4")], [], Window);
        Assert.NotEqual(r[0].IncidentId, r[1].IncidentId);
    }

    [Fact]
    public void Ids_Deterministic()
    {
        var input = new[] { A("a2", 3), A("a1", 0) };
        var first = IncidentCorrelator.Assign(input, [], Window);
        var second = IncidentCorrelator.Assign(input.Reverse().ToList(), [], Window);
        Assert.Equal(first.OrderBy(x => x.AlertId).ToList(), second.OrderBy(x => x.AlertId).ToList());
        Assert.Matches("^INC-[0-9a-f]{12}$", first[0].IncidentId);
    }

    [Fact]
    public void Derive_TwoRules_EscalatedHigh()
    {
        var d = IncidentCorrelator.Derive("ip:1.1.1.1",
        [
            new("a", T0, "Unauthorised Endpoint Access", "Medium", "OPEN", "ip:1.1.1.1"),
            new("b", T0.AddMinutes(5), "Multiple Failed Logins", "Low", "RESOLVED", "ip:1.1.1.1"),
        ]);
        Assert.True(d.Escalated);
        Assert.Equal("HIGH", d.Priority);
        Assert.Equal("Medium", d.Severity);
        Assert.Equal("OPEN", d.Status);
        Assert.Equal("Activity from ip:1.1.1.1: Multiple Failed Logins, Unauthorised Endpoint Access", d.Title);
        Assert.Equal(T0, d.OpenedAt);
        Assert.Equal(T0.AddMinutes(5), d.LastAlertAt);
    }
}
