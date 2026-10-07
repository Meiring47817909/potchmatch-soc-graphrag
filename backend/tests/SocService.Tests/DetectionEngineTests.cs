using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SocService.Data;
using SocService.Detection;
using SocService.Models;

namespace SocService.Tests;

public class DetectionEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static SocEvent Ev(DateTimeOffset ts, string type = "FAILED_LOGIN") => new()
    {
        EventId = Guid.NewGuid(), Timestamp = ts, ReceivedAt = ts, ServiceName = "identity-service",
        EventType = type, Severity = "MEDIUM", Message = "m",
    };

    private static DetectionEngine Engine(SocDbContext db, params IDetectionRule[] rules) =>
        new(db, rules, new AlertWriter(db), NullLogger<DetectionEngine>.Instance);

    private sealed class ThrowingRule : FakeRule
    {
        public ThrowingRule() { Matcher = _ => throw new InvalidOperationException("boom"); }
    }

    [Fact]
    public async Task Engine_DoesNotReopen_ResolvedAlert_FromSameEvents()
    {
        using var db = TestDb.Create();
        var events = new[] { Ev(Now.AddMinutes(-1)), Ev(Now.AddMinutes(-2)) };
        db.Events.AddRange(events);
        await db.SaveChangesAsync();
        var rule = new FakeRule
        {
            Matcher = evs => evs.Count == 0 ? [] :
                [new RuleMatch("k", "d", null, null, null, evs.Select(e => e.EventId).ToList())],
        };
        var engine = Engine(db, rule);

        await engine.RunOnceAsync(Now, default);
        var alert = await db.Alerts.SingleAsync();
        alert.Status = SocConstants.Status.Resolved;
        await db.SaveChangesAsync();

        await engine.RunOnceAsync(Now.AddSeconds(15), default);

        var only = await db.Alerts.SingleAsync();
        Assert.Equal(SocConstants.Status.Resolved, only.Status);
        Assert.Empty(rule.Received.Last());
    }

    [Fact]
    public async Task Engine_FailingRule_DoesNotStopOtherRules()
    {
        using var db = TestDb.Create();
        var e = Ev(Now.AddMinutes(-1));
        db.Events.Add(e);
        await db.SaveChangesAsync();
        var good = new FakeRule
        {
            RuleName = SocConstants.Rules.UnauthorisedAccess,
            Matcher = evs => [new RuleMatch("g", "d", null, null, null, evs.Select(x => x.EventId).ToList())],
        };

        await Engine(db, new ThrowingRule(), good).RunOnceAsync(Now, default);

        var alert = await db.Alerts.SingleAsync();
        Assert.Equal("g", alert.DedupKey);
    }

    [Fact]
    public async Task Engine_OnlyPassesEventsInsideWindow()
    {
        using var db = TestDb.Create();
        var inside = Ev(Now.AddMinutes(-4));
        var outside = Ev(Now.AddMinutes(-6));
        var wrongType = Ev(Now.AddMinutes(-1), "OTHER");
        db.Events.AddRange(inside, outside, wrongType);
        await db.SaveChangesAsync();
        var rule = new FakeRule();

        await Engine(db, rule).RunOnceAsync(Now, default);

        var passed = Assert.Single(rule.Received);
        Assert.Equal(inside.EventId, Assert.Single(passed).EventId);
    }

    [Fact]
    public async Task Engine_RecoveredEvent_ResolvesServiceFailureAlert()
    {
        using var db = TestDb.Create();
        var rule = new SocService.Detection.Rules.ServiceFailureRule(
            Microsoft.Extensions.Options.Options.Create(new DetectionOptions()));
        var critical = Ev(Now.AddSeconds(-40), "SERVICE_HEALTH_CRITICAL");
        critical.AffectedEntity = "payment-service";
        db.Events.Add(critical);
        await db.SaveChangesAsync();
        var engine = Engine(db, rule);

        await engine.RunOnceAsync(Now.AddSeconds(-30), default);
        Assert.Equal(SocConstants.Status.Open, (await db.Alerts.SingleAsync()).Status);

        var recovered = Ev(Now.AddSeconds(-10), "SERVICE_HEALTH_RECOVERED");
        recovered.AffectedEntity = "payment-service";
        db.Events.Add(recovered);
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now, default);

        db.ChangeTracker.Clear();
        var alert = await db.Alerts.Include(a => a.AlertEvents).SingleAsync();
        Assert.Equal(SocConstants.Status.Resolved, alert.Status);
        Assert.DoesNotContain(alert.AlertEvents, ae => ae.EventId == recovered.EventId);
    }

    private static SocEvent Health(DateTimeOffset ts, string type) 
    {
        var e = Ev(ts, type);
        e.AffectedEntity = "payment-service";
        return e;
    }

    private static DetectionEngine ServiceEngine(SocDbContext db) =>
        Engine(db, new SocService.Detection.Rules.ServiceFailureRule(
            Microsoft.Extensions.Options.Options.Create(new DetectionOptions())));

    [Fact]
    public async Task Engine_AfterResolve_DoesNotRecreateAlert()
    {
        using var db = TestDb.Create();
        var engine = ServiceEngine(db);
        db.Events.Add(Health(Now.AddSeconds(-40), "SERVICE_HEALTH_CRITICAL"));
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now.AddSeconds(-30), default);
        Assert.Equal(SocConstants.Status.Open, (await db.Alerts.SingleAsync()).Status);

        db.Events.Add(Health(Now.AddSeconds(-10), "SERVICE_HEALTH_RECOVERED"));
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now, default);
        await engine.RunOnceAsync(Now.AddSeconds(15), default);

        db.ChangeTracker.Clear();
        Assert.Equal(SocConstants.Status.Resolved, (await db.Alerts.SingleAsync()).Status);
    }

    [Fact]
    public async Task Engine_NewCriticalAfterResolve_OpensSecondAlert()
    {
        using var db = TestDb.Create();
        var engine = ServiceEngine(db);
        db.Events.Add(Health(Now.AddSeconds(-60), "SERVICE_HEALTH_CRITICAL"));
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now.AddSeconds(-50), default);
        db.Events.Add(Health(Now.AddSeconds(-40), "SERVICE_HEALTH_RECOVERED"));
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now.AddSeconds(-30), default);

        var c2 = Health(Now.AddSeconds(-10), "SERVICE_HEALTH_CRITICAL");
        db.Events.Add(c2);
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now, default);

        db.ChangeTracker.Clear();
        var alerts = await db.Alerts.Include(a => a.AlertEvents).OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal(SocConstants.Status.Resolved, alerts[0].Status);
        Assert.Equal(SocConstants.Status.Open, alerts[1].Status);
        Assert.Equal(c2.EventId, Assert.Single(alerts[1].AlertEvents).EventId);
    }

    [Fact]
    public async Task Engine_RecoveryAndNewCriticalInSameCycle_ResolvesOldAndOpensNew()
    {
        using var db = TestDb.Create();
        var engine = ServiceEngine(db);
        db.Events.Add(Health(Now.AddSeconds(-60), "SERVICE_HEALTH_CRITICAL"));
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now.AddSeconds(-50), default);

        var c2 = Health(Now.AddSeconds(-10), "SERVICE_HEALTH_CRITICAL");
        db.Events.AddRange(Health(Now.AddSeconds(-30), "SERVICE_HEALTH_RECOVERED"), c2);
        await db.SaveChangesAsync();
        await engine.RunOnceAsync(Now, default);

        db.ChangeTracker.Clear();
        var alerts = await db.Alerts.Include(a => a.AlertEvents).OrderBy(a => a.Timestamp).ToListAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal(SocConstants.Status.Resolved, alerts[0].Status);
        Assert.Equal(SocConstants.Status.Open, alerts[1].Status);
        Assert.Equal(c2.EventId, Assert.Single(alerts[1].AlertEvents).EventId);
    }
}
