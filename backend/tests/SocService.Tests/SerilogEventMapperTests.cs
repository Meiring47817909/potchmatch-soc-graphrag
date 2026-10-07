using SocService.Ingestion;

namespace SocService.Tests;

public class SerilogEventMapperTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 7, 12, 0, 5, TimeSpan.Zero);

    [Fact]
    public void Mapper_MapsAllSpecFields()
    {
        var id = Guid.NewGuid();
        var env = EnvelopeBuilder.Make(id);

        Assert.True(SerilogEventMapper.TryMap(env, Received, out var evt, out var error));
        Assert.Null(error);
        Assert.NotNull(evt);
        Assert.Equal(id, evt!.EventId);
        Assert.Equal(env.Timestamp!.Value, evt.Timestamp);
        Assert.Equal(Received, evt.ReceivedAt);
        Assert.Equal("LOGIN_FAILED", evt.EventType);
        Assert.Equal("HIGH", evt.Severity);
        Assert.Equal("auth-service", evt.ServiceName);
        Assert.Equal("user-1", evt.UserId);
        Assert.Equal("192.168.1.9", evt.SourceIp);
        Assert.Equal("/api/auth/login", evt.Endpoint);
        Assert.Equal("POST", evt.HttpMethod);
        Assert.Equal(401, evt.StatusCode);
        Assert.Equal("Login failed", evt.Message);
        Assert.Equal("corr-1", evt.CorrelationId);
        Assert.Equal("user:1", evt.AffectedEntity);
    }

    [Fact]
    public void Mapper_UsesClientIp_WhenSourceIpMissing()
    {
        var env = EnvelopeBuilder.Make(overrides: new() { ["ClientIp"] = "10.0.0.5" }, remove: new[] { "SourceIp" });
        Assert.True(SerilogEventMapper.TryMap(env, Received, out var evt, out _));
        Assert.Equal("10.0.0.5", evt!.SourceIp);
    }

    [Fact]
    public void Mapper_Rejects_InvalidEventId()
    {
        var env = EnvelopeBuilder.Make(overrides: new() { ["EventId"] = "not-a-guid" });
        Assert.False(SerilogEventMapper.TryMap(env, Received, out var evt, out var error));
        Assert.Null(evt);
        Assert.NotNull(error);
    }

    [Fact]
    public void Mapper_Rejects_MissingEventType()
    {
        var env = EnvelopeBuilder.Make(remove: new[] { "EventType" });
        Assert.False(SerilogEventMapper.TryMap(env, Received, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Mapper_Rejects_UnknownSeverity()
    {
        var env = EnvelopeBuilder.Make(overrides: new() { ["Severity"] = "CRITICAL" });
        Assert.False(SerilogEventMapper.TryMap(env, Received, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Mapper_NormalisesTimestampToUtc()
    {
        var env = EnvelopeBuilder.Make();
        env.Timestamp = new DateTimeOffset(2026, 10, 7, 14, 0, 0, TimeSpan.FromHours(2));
        Assert.True(SerilogEventMapper.TryMap(env, Received, out var evt, out _));
        Assert.Equal(TimeSpan.Zero, evt!.Timestamp.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), evt.Timestamp);
    }

    [Fact]
    public void Mapper_Rejects_MissingTimestamp()
    {
        var env = EnvelopeBuilder.Make();
        env.Timestamp = null;
        Assert.False(SerilogEventMapper.TryMap(env, Received, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Mapper_Rejects_DefaultTimestamp()
    {
        var env = EnvelopeBuilder.Make();
        env.Timestamp = default(DateTimeOffset);
        Assert.False(SerilogEventMapper.TryMap(env, Received, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Mapper_Rejects_NullProperties()
    {
        var env = EnvelopeBuilder.Make();
        env.Properties = null!;
        Assert.False(SerilogEventMapper.TryMap(env, Received, out _, out var error));
        Assert.NotNull(error);
    }
}
