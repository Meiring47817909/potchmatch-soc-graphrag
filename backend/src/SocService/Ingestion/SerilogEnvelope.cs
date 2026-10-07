using System.Text.Json;

namespace SocService.Ingestion;

/// <summary>
/// One log event as posted by Serilog.Sinks.Http's default
/// NormalRenderedTextFormatter. The request body is a JSON array of these.
/// </summary>
public class SerilogEnvelope
{
    public DateTimeOffset Timestamp { get; set; }
    public string? Level { get; set; }
    public string? RenderedMessage { get; set; }
    public Dictionary<string, JsonElement> Properties { get; set; } = new();
}
