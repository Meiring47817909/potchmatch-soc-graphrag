using SocService.Models;
using SocService.Queries;

namespace SocService.Tests;

public class AlertsQueryTests
{
    private static SocEvent Ev(string type, string? user, DateTimeOffset ts) => new()
    {
        EventId = Guid.NewGuid(), Timestamp = ts, ReceivedAt = ts, ServiceName = "svc",
        EventType = type, Severity = "LOW", UserId = user, Message = "m",
    };

    [Fact]
    public async Task EventsQuery_FiltersByTypeAndUser_NewestFirst()
    {
        using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;
        var old = Ev("LOGIN_FAILED", "a@x.com", now.AddMinutes(-10));
        var recent = Ev("LOGIN_FAILED", "a@x.com", now.AddMinutes(-1));
        db.Events.AddRange(old, recent,
            Ev("LOGIN_FAILED", "b@x.com", now),
            Ev("UNAUTHORISED_ACCESS", "a@x.com", now));
        await db.SaveChangesAsync();

        var result = await EventsQuery.RunAsync(db, "LOGIN_FAILED", "a@x.com", null, null, 1, 50);

        Assert.Equal(new[] { recent.EventId, old.EventId }, result.Select(r => r.EventId));
    }

    [Fact]
    public async Task EventsQuery_PagesAndCapsPageSize()
    {
        using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++) db.Events.Add(Ev("X", null, now.AddMinutes(-i)));
        await db.SaveChangesAsync();

        var page2 = await EventsQuery.RunAsync(db, null, null, null, null, 2, 2);
        Assert.Equal(2, page2.Count);
        Assert.Equal(200, EventsQuery.NormalisePageSize(10_000));
        Assert.Equal(50, EventsQuery.NormalisePageSize(0));
    }

    [Fact]
    public async Task EventsQuery_AcceptsNonUtcOffsetBounds()
    {
        using var db = TestDb.Create();
        var t = new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
        var before = Ev("X", null, t.AddMinutes(-30));
        var inside = Ev("X", null, t.AddMinutes(10));
        var after = Ev("X", null, t.AddHours(2));
        db.Events.AddRange(before, inside, after);
        await db.SaveChangesAsync();

        // 12:00+02:00 == 10:00Z ; 13:00+02:00 == 11:00Z
        var from = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(2));
        var to = new DateTimeOffset(2026, 10, 7, 13, 0, 0, TimeSpan.FromHours(2));
        var result = await EventsQuery.RunAsync(db, null, null, from, to, 1, 50);

        Assert.Equal(inside.EventId, Assert.Single(result).EventId);
    }

    [Fact]
    public async Task EventsQuery_ExcludeType_HidesThatType()
    {
        using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;
        var keep = Ev("LOGIN_FAILED", "a@x.com", now);
        db.Events.AddRange(keep, Ev("REQUEST_RECEIVED", "a@x.com", now.AddSeconds(-1)));
        await db.SaveChangesAsync();

        var result = await EventsQuery.RunAsync(db, null, null, null, null, 1, 50, excludeType: "REQUEST_RECEIVED");

        Assert.Equal(keep.EventId, Assert.Single(result).EventId);
    }

    [Fact]
    public async Task EventsQuery_AlertId_ReturnsOnlyLinkedEvents()
    {
        using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;
        var linked1 = Ev("LOGIN_FAILED", "a@x.com", now.AddMinutes(-2));
        var linked2 = Ev("LOGIN_FAILED", "a@x.com", now.AddMinutes(-1));
        var other = Ev("LOGIN_FAILED", "a@x.com", now);
        db.Events.AddRange(linked1, linked2, other);
        var alertId = Guid.NewGuid();
        db.AlertEvents.AddRange(
            new AlertEvent { AlertId = alertId, EventId = linked1.EventId },
            new AlertEvent { AlertId = alertId, EventId = linked2.EventId },
            new AlertEvent { AlertId = Guid.NewGuid(), EventId = other.EventId });
        await db.SaveChangesAsync();

        var result = await EventsQuery.RunAsync(db, null, null, null, null, 1, 50, alertId: alertId);

        Assert.Equal(new[] { linked2.EventId, linked1.EventId }, result.Select(r => r.EventId));
    }
}
