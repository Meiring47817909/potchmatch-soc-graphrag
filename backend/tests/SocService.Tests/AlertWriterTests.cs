using Microsoft.EntityFrameworkCore;
using SocService.Detection;
using SocService.Models;

namespace SocService.Tests;

public class AlertWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static RuleMatch Match(string key, params Guid[] ids) =>
        new(key, "desc", "svc", "user", "entity", ids);

    [Fact]
    public async Task Writer_CreatesOpenAlert_WithLinkedEvents()
    {
        using var db = TestDb.Create();
        var writer = new AlertWriter(db);
        var e1 = Guid.NewGuid(); var e2 = Guid.NewGuid();

        var alert = await writer.ApplyAsync(new FakeRule(), Match("k", e1, e2), Now, default);

        Assert.Equal(SocConstants.Status.Open, alert.Status);
        Assert.Equal(SocConstants.Severity.High, alert.Severity);
        Assert.Equal("Do the thing.", alert.RecommendedAction);
        Assert.Equal(SocConstants.Rules.FailedLogins, alert.RuleName);
        Assert.Equal("k", alert.DedupKey);
        Assert.Equal("svc", alert.AffectedService);
        Assert.Equal("user", alert.AffectedUser);
        Assert.Equal("entity", alert.AffectedEntity);
        Assert.Equal(Now, alert.Timestamp);
        var links = await db.AlertEvents.Where(a => a.AlertId == alert.AlertId).Select(a => a.EventId).ToListAsync();
        Assert.Equal(new[] { e1, e2 }.OrderBy(x => x), links.OrderBy(x => x));
    }

    [Fact]
    public async Task Writer_OpenAlert_AbsorbsNewEvents_NoDuplicateLinks()
    {
        using var db = TestDb.Create();
        var writer = new AlertWriter(db);
        Guid e1 = Guid.NewGuid(), e2 = Guid.NewGuid(), e3 = Guid.NewGuid();

        await writer.ApplyAsync(new FakeRule(), Match("k", e1, e2), Now, default);
        var later = Now.AddMinutes(1);
        await writer.ApplyAsync(new FakeRule(), Match("k", e2, e3), later, default);

        var alert = await db.Alerts.SingleAsync();
        Assert.Equal(later, alert.UpdatedAt);
        Assert.Equal(Now, alert.Timestamp);
        var links = await db.AlertEvents.Select(a => a.EventId).ToListAsync();
        Assert.Equal(3, links.Count);
        Assert.Equal(new[] { e1, e2, e3 }.OrderBy(x => x), links.OrderBy(x => x));
    }

    [Fact]
    public async Task Writer_AcknowledgedAlert_AlsoAbsorbs()
    {
        using var db = TestDb.Create();
        var writer = new AlertWriter(db);
        await writer.ApplyAsync(new FakeRule(), Match("k", Guid.NewGuid()), Now, default);
        (await db.Alerts.SingleAsync()).Status = SocConstants.Status.Acknowledged;
        await db.SaveChangesAsync();

        await writer.ApplyAsync(new FakeRule(), Match("k", Guid.NewGuid()), Now.AddMinutes(1), default);

        var alert = await db.Alerts.SingleAsync();
        Assert.Equal(SocConstants.Status.Acknowledged, alert.Status);
        Assert.Equal(2, await db.AlertEvents.CountAsync());
    }

    [Fact]
    public async Task Writer_ResolvedAlert_DoesNotAbsorb_NewAlertCreated()
    {
        using var db = TestDb.Create();
        var writer = new AlertWriter(db);
        await writer.ApplyAsync(new FakeRule(), Match("k", Guid.NewGuid()), Now, default);
        (await db.Alerts.SingleAsync()).Status = SocConstants.Status.Resolved;
        await db.SaveChangesAsync();

        var fresh = await writer.ApplyAsync(new FakeRule(), Match("k", Guid.NewGuid()), Now.AddMinutes(1), default);

        Assert.Equal(2, await db.Alerts.CountAsync());
        Assert.Equal(SocConstants.Status.Open, fresh.Status);
        Assert.Equal(1, await db.AlertEvents.CountAsync(a => a.AlertId == fresh.AlertId));
    }

    [Fact]
    public async Task Writer_Resolve_OnlyResolvesOlderAlerts()
    {
        using var db = TestDb.Create();
        var writer = new AlertWriter(db);
        var old = await writer.ApplyAsync(new FakeRule(), Match("k", Guid.NewGuid()), Now, default);
        // Same key but created later: a distinct OPEN alert is needed, so resolve the first and add another.
        old.Status = SocConstants.Status.Acknowledged;
        await db.SaveChangesAsync();
        db.Alerts.Add(new Alert
        {
            AlertId = Guid.NewGuid(), Timestamp = Now.AddMinutes(10), UpdatedAt = Now.AddMinutes(10),
            RuleName = "r", Severity = "HIGH", Status = SocConstants.Status.Open, DedupKey = "k",
        });
        db.Alerts.Add(new Alert
        {
            AlertId = Guid.NewGuid(), Timestamp = Now.AddMinutes(-10), UpdatedAt = Now,
            RuleName = "r", Severity = "HIGH", Status = SocConstants.Status.Open, DedupKey = "other",
        });
        await db.SaveChangesAsync();

        var count = await writer.ResolveAsync("k", Now.AddMinutes(5), default);

        Assert.Equal(1, count);
        Assert.Equal(SocConstants.Status.Resolved, (await db.Alerts.SingleAsync(a => a.AlertId == old.AlertId)).Status);
        Assert.Equal(SocConstants.Status.Open, (await db.Alerts.SingleAsync(a => a.Timestamp == Now.AddMinutes(10))).Status);
        Assert.Equal(SocConstants.Status.Open, (await db.Alerts.SingleAsync(a => a.DedupKey == "other")).Status);
    }
}
