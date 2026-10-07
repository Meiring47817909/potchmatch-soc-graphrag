using System.Text.Json;
using SocService.Dtos;
using SocService.Models;

namespace SocService.Tests;

public class AlertResponseTests
{
    [Fact]
    public void AlertResponse_From_MapsFieldsAndRelatedEvents()
    {
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        var alert = new Alert
        {
            AlertId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RuleName = SocConstants.Rules.FailedLogins,
            Severity = SocConstants.Severity.High,
            Status = SocConstants.Status.Open,
            Description = "d",
            AffectedService = "identity-service",
            AffectedUser = "u@x.com",
            AffectedEntity = "/api/auth/login",
            RecommendedAction = "act",
            AlertEvents = { new AlertEvent { EventId = e1 }, new AlertEvent { EventId = e2 } },
        };
        // e2 happened before e1, so it must come first.
        var times = new Dictionary<Guid, DateTimeOffset>
        {
            [e1] = DateTimeOffset.UtcNow,
            [e2] = DateTimeOffset.UtcNow.AddMinutes(-5),
        };

        var dto = AlertResponse.From(alert, times);
        Assert.Equal(new[] { e2, e1 }, dto.RelatedEvents);
        Assert.Equal("identity-service", dto.AffectedService);

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        var expected = new[] { "alertId", "timestamp", "ruleName", "severity", "status", "description",
            "relatedEvents", "affectedService", "affectedUser", "affectedEntity", "recommendedAction" }
            .OrderBy(n => n).ToArray();
        Assert.Equal(expected, names);
    }

    [Theory]
    [InlineData("OPEN", false)]
    [InlineData("closed", false)]
    [InlineData("acknowledged", false)]
    [InlineData("ACKNOWLEDGED", true)]
    [InlineData("RESOLVED", true)]
    public void AlertStatusUpdate_RejectsOpen(string status, bool valid)
    {
        Assert.Equal(valid, AlertStatusUpdate.IsValidTarget(status));
    }
}
