using Microsoft.Extensions.Options;
using SocService.Detection.Rules;
using SocService.Models;

namespace SocService.Tests;

public class MultipleFailedLoginsRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static MultipleFailedLoginsRule NewRule() => new(Options.Create(new SocService.Detection.DetectionOptions()));

    private static SocEvent Failed(string? user, string? ip, TimeSpan age) => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = Now - age,
        ServiceName = "identity-service",
        EventType = "FAILED_LOGIN",
        Severity = SocConstants.Severity.Medium,
        UserId = user,
        SourceIp = ip,
    };

    [Fact]
    public void FailedLogins_FourAttempts_NoMatch()
    {
        var events = Enumerable.Range(0, 4).Select(_ => Failed("a@b.com", "1.1.1.1", TimeSpan.FromMinutes(1))).ToList();
        Assert.Empty(NewRule().Evaluate(events, Now));
    }

    [Fact]
    public void FailedLogins_FiveAttempts_SameAccount_Match()
    {
        var events = Enumerable.Range(0, 5).Select(_ => Failed("wickusvn@gmail.com", null, TimeSpan.FromMinutes(1))).ToList();
        var match = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal(5, match.EventIds.Count);
        Assert.Equal("Multiple Failed Logins:account:wickusvn@gmail.com", match.DedupKey);
        Assert.Equal("wickusvn@gmail.com", match.AffectedUser);
        Assert.Equal("identity-service", match.AffectedService);
        Assert.Equal("/api/auth/login", match.AffectedEntity);
        Assert.Equal("5 failed login attempts for wickusvn@gmail.com within 5 minutes.", match.Description);
    }

    [Fact]
    public void FailedLogins_FiveAccounts_SameIp_IpMatch()
    {
        var events = Enumerable.Range(0, 5).Select(i => Failed($"user{i}@x.com", "9.9.9.9", TimeSpan.FromMinutes(1))).ToList();
        var match = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal("Multiple Failed Logins:ip:9.9.9.9", match.DedupKey);
        Assert.Null(match.AffectedUser);
        Assert.Equal("5 failed login attempts for IP 9.9.9.9 within 5 minutes.", match.Description);
    }

    [Fact]
    public void FailedLogins_EventsOutsideWindow_Ignored()
    {
        var events = Enumerable.Range(0, 4).Select(_ => Failed("a@b.com", null, TimeSpan.FromMinutes(1))).ToList();
        events.Add(Failed("a@b.com", null, TimeSpan.FromMinutes(6)));
        Assert.Empty(NewRule().Evaluate(events, Now));
    }

    [Fact]
    public void FailedLogins_NormalisesEmailCase()
    {
        var users = new[] { "Wickusvn@Gmail.com ", "wickusvn@gmail.com", "WICKUSVN@GMAIL.COM", "wickusvn@gmail.com", " wickusvn@gmail.com" };
        var events = users.Select(u => Failed(u, null, TimeSpan.FromMinutes(1))).ToList();
        var match = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal("Multiple Failed Logins:account:wickusvn@gmail.com", match.DedupKey);
    }
}
