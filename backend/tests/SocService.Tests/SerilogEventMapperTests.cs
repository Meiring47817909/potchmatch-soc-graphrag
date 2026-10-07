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

    // Captured verbatim from a real Serilog.Sinks.Http 9.2.1 durable buffer line
    // (NormalRenderedTextFormatter) written by api-gateway: flat Properties, null
    // values for unset fields, extra bookkeeping properties, SecurityEvent as a bool.
    private const string RealSinkLine =
        """{"Timestamp":"2026-10-07T21:01:36.2204213Z","Level":"Information","MessageTemplate":"{EventType} [{Severity}] {Message}","RenderedMessage":"\"REQUEST_RECEIVED\" [\"LOW\"] \"POST /api/auth/login received\"","TraceId":"ed69893a28e9e587b8f73c7c77a28ca4","SpanId":"de4a32d4343a384f","Properties":{"EventType":"REQUEST_RECEIVED","Severity":"LOW","Message":"POST /api/auth/login received","UserId":null,"SourceIp":"172.18.0.1","Endpoint":"/api/auth/login","HttpMethod":"POST","StatusCode":null,"AffectedEntity":null,"SourceContext":"Program","RequestId":"0HNP4MOMAC706:00000001","SecurityEvent":true,"EventId":"f5d21c3b-4f35-460f-9ff5-e4a266a118d5","ClientIp":"172.18.0.1","CorrelationId":"93aef766-3e1c-47fe-a0ce-662992bbbeba","ServiceName":"api-gateway"}}""";

    [Fact]
    public void Mapper_MapsRealSerilogSinkHttpPayload()
    {
        var batch = System.Text.Json.JsonSerializer.Deserialize<List<SerilogEnvelope>>(
            "[" + RealSinkLine + "]",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.True(SerilogEventMapper.TryMap(batch![0], Received, out var evt, out var error), error);
        Assert.Equal(Guid.Parse("f5d21c3b-4f35-460f-9ff5-e4a266a118d5"), evt!.EventId);
        Assert.Equal("REQUEST_RECEIVED", evt.EventType);
        Assert.Equal("LOW", evt.Severity);
        Assert.Equal("api-gateway", evt.ServiceName);
        Assert.Equal("172.18.0.1", evt.SourceIp);
        Assert.Equal("/api/auth/login", evt.Endpoint);
        Assert.Equal("POST", evt.HttpMethod);
        Assert.Null(evt.UserId);
        Assert.Null(evt.StatusCode);
        Assert.Equal("93aef766-3e1c-47fe-a0ce-662992bbbeba", evt.CorrelationId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-07T21:01:36.2204213Z"), evt.Timestamp);
    }
}
