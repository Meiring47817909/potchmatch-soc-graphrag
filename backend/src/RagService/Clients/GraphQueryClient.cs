using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RagService.Options;

namespace RagService.Clients;

/// <summary>Typed client for GraphService's internal query/schema endpoints (header X-Graph-Query-Key).</summary>
public sealed class GraphQueryClient(HttpClient http, IOptions<GraphQueryOptions> options) : IGraphQueryClient
{
    public const string KeyHeader = "X-Graph-Query-Key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record ErrorBody(string? Error, string? Reason);

    public async Task<GraphSchemaDto> GetSchemaAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/internal/schema");
        using var response = await SendAsync(request, ct);
        return await ReadAsync<GraphSchemaDto>(response, ct);
    }

    public async Task<GraphQueryOutcome> QueryAsync(string cypher, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/internal/query")
        {
            Content = JsonContent.Create(new { cypher, parameters }, options: Json),
        };
        using var response = await SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            ErrorBody? err = null;
            try { err = await response.Content.ReadFromJsonAsync<ErrorBody>(Json, ct); }
            catch (JsonException) { }
            return new GraphQueryOutcome(false, null, err?.Error ?? "rejected", err?.Reason);
        }

        var evidence = await ReadAsync<GraphEvidence>(response, ct);
        return new GraphQueryOutcome(true, evidence, null, null);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add(KeyHeader, options.Value.Key);
        try
        {
            var response = await http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.BadRequest || response.IsSuccessStatusCode) return response;
            var code = (int)response.StatusCode;
            response.Dispose();
            throw new GraphUnavailableException($"GraphService returned HTTP {code}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GraphUnavailableException("GraphService request timed out", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new GraphUnavailableException("GraphService request failed", ex);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                ?? throw new GraphUnavailableException("GraphService returned an empty response");
        }
        catch (JsonException ex)
        {
            throw new GraphUnavailableException("GraphService returned an unreadable response", ex);
        }
    }
}
