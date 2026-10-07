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
}
