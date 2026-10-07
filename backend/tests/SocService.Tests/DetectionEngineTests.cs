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
}
