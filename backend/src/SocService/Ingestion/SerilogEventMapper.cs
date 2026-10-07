using System.Text.Json;
using SocService.Models;

namespace SocService.Ingestion;

public static class SerilogEventMapper
{
    public static bool TryMap(SerilogEnvelope env, DateTimeOffset receivedAt, out SocEvent? evt, out string? error)
    {
        evt = null;
        error = null;
        var props = env.Properties ?? new Dictionary<string, JsonElement>();

        if (!Guid.TryParse(GetString(props, "EventId"), out var eventId))
        {
            error = "EventId missing or not a GUID";
            return false;
        }

        var eventType = GetString(props, "EventType");
        if (string.IsNullOrWhiteSpace(eventType))
        {
            error = "EventType missing";
            return false;
        }

        var serviceName = GetString(props, "ServiceName");
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            error = "ServiceName missing";
            return false;
        }

        var severity = GetString(props, "Severity")?.Trim().ToUpperInvariant();
        if (severity is not (SocConstants.Severity.Low or SocConstants.Severity.Medium or SocConstants.Severity.High))
        {
            error = "Severity must be LOW, MEDIUM or HIGH";
            return false;
        }

        var sourceIp = GetString(props, "SourceIp");
        if (string.IsNullOrEmpty(sourceIp))
            sourceIp = GetString(props, "ClientIp");

        int? statusCode = null;
        if (props.TryGetValue("StatusCode", out var sc))
        {
            if (sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out var n)) statusCode = n;
            else if (sc.ValueKind == JsonValueKind.String && int.TryParse(sc.GetString(), out var s)) statusCode = s;
        }

        evt = new SocEvent
        {
            EventId = eventId,
            Timestamp = env.Timestamp,
            ReceivedAt = receivedAt,
            ServiceName = serviceName,
            EventType = eventType,
            Severity = severity,
            UserId = GetString(props, "UserId"),
            SourceIp = sourceIp,
            Endpoint = GetString(props, "Endpoint"),
            HttpMethod = GetString(props, "HttpMethod"),
            StatusCode = statusCode,
            Message = GetString(props, "Message") ?? env.RenderedMessage ?? string.Empty,
            CorrelationId = GetString(props, "CorrelationId"),
            AffectedEntity = GetString(props, "AffectedEntity"),
        };
        return true;
    }

    private static string? GetString(Dictionary<string, JsonElement> props, string key)
    {
        if (!props.TryGetValue(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.GetRawText(),
        };
    }
}
