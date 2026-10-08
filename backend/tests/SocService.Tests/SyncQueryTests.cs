using SocService.Models;
using SocService.Queries;

namespace SocService.Tests;

public class SyncQueryTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Test data sits in the past relative to this clock, so the settle lag never hides it.
    private static SyncQuery Query(SocService.Data.SocDbContext db, DateTimeOffset? now = null) =>
        new(db, new FixedTime(now ?? DateTimeOffset.UtcNow.AddHours(1)));

    private static SocEvent Ev(string type, DateTimeOffset ts, DateTimeOffset? received = null) => new()
    {
        EventId = Guid.NewGuid(), Timestamp = ts, ReceivedAt = received ?? ts, ServiceName = "svc",
        EventType = type, Severity = "LOW", Message = "m",
    };

    private static Alert Al(DateTimeOffset updated, params SocEvent[] events) => new()
    {
        AlertId = Guid.NewGuid(), Timestamp = updated, UpdatedAt = updated, RuleName = "Multiple Failed Logins",
        Severity = "HIGH", Status = "OPEN", Description = "d", RecommendedAction = "r", DedupKey = Guid.NewGuid().ToString(),
        AlertEvents = events.Select(e => new AlertEvent { EventId = e.EventId }).ToList(),
    };

    [Fact]
    public async Task SyncAlerts_ReturnsUpdatedSinceInOrderWithFullEvents()
    {
        using var db = TestDb.Create();
        var t1 = DateTimeOffset.UtcNow.AddHours(-3);
        var t2 = t1.AddHours(1);
        var t3 = t1.AddHours(2);
        var e1 = Ev("FAILED_LOGIN", t1.AddMinutes(5));
        var e2 = Ev("FAILED_LOGIN", t1.AddMinutes(1));
        db.Events.AddRange(e1, e2);
        var a1 = Al(t1); var a2 = Al(t2, e1, e2); var a3 = Al(t3);
        db.Alerts.AddRange(a3, a1, a2);
        await db.SaveChangesAsync();

        var result = await Query(db).GetAlertsAsync(t1, null, 200, default);

        Assert.Equal(new[] { a2.AlertId, a3.AlertId }, result.Select(r => r.AlertId));
        Assert.Equal(new[] { e2.EventId, e1.EventId }, result[0].RelatedEvents.Select(e => e.EventId));
        Assert.Equal("FAILED_LOGIN", result[0].RelatedEvents[0].EventType);
        Assert.Empty(result[1].RelatedEvents);
    }

    [Fact]
    public async Task SyncAlerts_TieAtPageBoundary_NothingSkipped()
    {
        using var db = TestDb.Create();
        var t = DateTimeOffset.UtcNow;
        db.Alerts.AddRange(Al(t), Al(t), Al(t));
        await db.SaveChangesAsync();
        var q = Query(db);

        var page1 = await q.GetAlertsAsync(null, null, 2, default);
        var last = page1[^1];
        var page2 = await q.GetAlertsAsync(last.UpdatedAt, last.AlertId, 2, default);

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
        Assert.Equal(3, page1.Concat(page2).Select(a => a.AlertId).Distinct().Count());
    }

    [Fact]
    public async Task SyncAlerts_LimitClamped()
    {
        using var db = TestDb.Create();
        var t = DateTimeOffset.UtcNow;
        for (var i = 0; i < 510; i++) db.Alerts.Add(Al(t.AddSeconds(i)));
        await db.SaveChangesAsync();

        var result = await Query(db).GetAlertsAsync(null, null, 10000, default);
        Assert.Equal(500, result.Count);
        var one = await Query(db).GetAlertsAsync(null, null, 0, default);
        Assert.Single(one);
    }

    [Fact]
    public async Task SyncEvents_ExcludesRequestReceivedAndAlertLinked()
    {
        using var db = TestDb.Create();
        var t = DateTimeOffset.UtcNow;
        var free = Ev("FAILED_LOGIN", t);
        var linked = Ev("FAILED_LOGIN", t);
        var req = Ev("REQUEST_RECEIVED", t);
        db.Events.AddRange(free, linked, req);
        db.Alerts.Add(Al(t, linked));
        await db.SaveChangesAsync();

        var result = await Query(db).GetEventsAsync(null, null, 500, default);

        var only = Assert.Single(result);
        Assert.Equal(free.EventId, only.Event.EventId);
        Assert.Equal(free.ReceivedAt, only.ReceivedAt);
    }

    [Fact]
    public async Task SyncEvents_TieAtPageBoundary_NothingSkipped()
    {
        using var db = TestDb.Create();
        var t = DateTimeOffset.UtcNow;
        db.Events.AddRange(Ev("X", t), Ev("X", t), Ev("X", t));
        await db.SaveChangesAsync();
        var q = Query(db);

        var page1 = await q.GetEventsAsync(null, null, 2, default);
        var page2 = await q.GetEventsAsync(page1[^1].ReceivedAt, page1[^1].Event.EventId, 2, default);

        Assert.Single(page2);
        Assert.Equal(3, page1.Concat(page2).Select(e => e.Event.EventId).Distinct().Count());
    }

    [Fact]
    public async Task SyncEvents_NonUtcCursor_Normalised()
    {
        using var db = TestDb.Create();
        var instant = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var before = Ev("X", instant.AddMinutes(-1));
        var after = Ev("X", instant.AddMinutes(1));
        db.Events.AddRange(before, after);
        await db.SaveChangesAsync();

        var cursor = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2)); // == 10:00Z
        var result = await Query(db).GetEventsAsync(cursor, null, 500, default);

        Assert.Equal(new[] { after.EventId }, result.Select(r => r.Event.EventId));
    }

    [Fact]
    public async Task SyncAlerts_RowsNewerThanSettleLag_Withheld()
    {
        using var db = TestDb.Create();
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var settled = Al(now.AddSeconds(-6));
        var fresh = Al(now.AddSeconds(-2));
        db.Alerts.AddRange(settled, fresh);
        await db.SaveChangesAsync();

        var result = await Query(db, now).GetAlertsAsync(null, null, 200, default);
        Assert.Equal(new[] { settled.AlertId }, result.Select(r => r.AlertId));

        var later = await Query(db, now.AddSeconds(5)).GetAlertsAsync(settled.UpdatedAt, settled.AlertId, 200, default);
        Assert.Equal(new[] { fresh.AlertId }, later.Select(r => r.AlertId));
    }

    [Fact]
    public async Task SyncEvents_RowsNewerThanSettleLag_Withheld()
    {
        using var db = TestDb.Create();
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var settled = Ev("X", now.AddSeconds(-30), now.AddSeconds(-5));
        var fresh = Ev("X", now.AddSeconds(-30), now.AddSeconds(-4));
        db.Events.AddRange(settled, fresh);
        await db.SaveChangesAsync();

        var result = await Query(db, now).GetEventsAsync(null, null, 500, default);
        Assert.Equal(new[] { settled.EventId }, result.Select(r => r.Event.EventId));
    }
}
