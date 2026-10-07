using Microsoft.EntityFrameworkCore;
using SocService.Models;

namespace SocService.Tests;

public class SocDbContextTests
{
    [Fact]
    public async Task CanStoreAlertLinkedToEvents()
    {
        using var db = TestDb.Create();
        var now = DateTimeOffset.UtcNow;
        var ev = new SocEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            ReceivedAt = now,
            ServiceName = "identity-service",
            EventType = "AUTH_LOGIN_FAILED",
            Severity = SocConstants.Severity.Medium,
            Message = "Login failed",
        };
        var alert = new Alert
        {
            AlertId = Guid.NewGuid(),
            Timestamp = now,
            UpdatedAt = now,
            RuleName = SocConstants.Rules.FailedLogins,
            Severity = SocConstants.Severity.High,
            Status = "OPEN",
            Description = "5 failed logins",
            RecommendedAction = "Block IP",
            DedupKey = "failed-logins:1.2.3.4",
        };
        db.Events.Add(ev);
        db.Alerts.Add(alert);
        db.AlertEvents.Add(new AlertEvent { AlertId = alert.AlertId, EventId = ev.EventId });
        await db.SaveChangesAsync();

        var loaded = await db.Alerts.Include(a => a.AlertEvents).SingleAsync(a => a.AlertId == alert.AlertId);

        Assert.Equal(ev.EventId, loaded.AlertEvents.Single().EventId);
    }
}
