using System.Text.Json;
using SocService.Ingestion;

namespace SocService.Tests;

public static class EnvelopeBuilder
{
    public static SerilogEnvelope Make(Guid? id = null, Dictionary<string, object?>? overrides = null, string[]? remove = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["EventId"] = (id ?? Guid.NewGuid()).ToString(),
            ["EventType"] = "LOGIN_FAILED",
            ["Severity"] = "high",
            ["ServiceName"] = "auth-service",
            ["UserId"] = "user-1",
            ["SourceIp"] = "192.168.1.9",
            ["Endpoint"] = "/api/auth/login",
            ["HttpMethod"] = "POST",
            ["StatusCode"] = 401,
            ["Message"] = "Login failed",
            ["CorrelationId"] = "corr-1",
            ["AffectedEntity"] = "user:1",
        };
        if (overrides != null) foreach (var kv in overrides) props[kv.Key] = kv.Value;
        if (remove != null) foreach (var k in remove) props.Remove(k);
        return new SerilogEnvelope
        {
            Timestamp = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
            Level = "Information",
            RenderedMessage = "rendered",
            Properties = props.Where(p => p.Value != null)
                .ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
        };
    }
}
