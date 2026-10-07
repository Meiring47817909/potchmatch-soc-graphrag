using Serilog.Sinks.Http;

namespace IdentityService.Logging;

// Serilog.Sinks.Http IHttpClient that POSTs batches to SocService's ingest
// endpoint, adding the shared X-Soc-Ingest-Key header from SocIngest:Key.
// Each service owns its own copy (per-service duplication convention).
public sealed class SocIngestHttpClient : IHttpClient
{
    private readonly HttpClient _httpClient = new();
    private readonly string? _key;

    public SocIngestHttpClient(IConfiguration appConfiguration)
    {
        _key = appConfiguration["SocIngest:Key"];
    }

    // Called by the sink with the sink's own configuration section (unused here).
    public void Configure(IConfiguration configuration)
    {
    }

    public async Task<HttpResponseMessage> PostAsync(string requestUri, Stream contentStream, CancellationToken cancellationToken)
    {
        using var content = new StreamContent(contentStream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri) { Content = content };
        if (!string.IsNullOrEmpty(_key))
        {
            request.Headers.Add("X-Soc-Ingest-Key", _key);
        }

        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _httpClient.Dispose();
}
