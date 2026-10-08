using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace GraphService.Sync;

public interface ISocSyncClient
{
    Task<IReadOnlyList<SyncAlert>> GetAlertsAsync(SyncCursor? after, int limit, CancellationToken ct);
    Task<IReadOnlyList<SyncEventEnvelope>> GetEventsAsync(SyncCursor? after, int limit, CancellationToken ct);
}

public class SocSyncClient : ISocSyncClient
{
    public const string KeyHeader = "X-Graph-Sync-Key";
    private readonly HttpClient _http;

    public SocSyncClient(HttpClient http, IOptions<GraphSyncOptions> options)
    {
        _http = http;
        _http.DefaultRequestHeaders.Remove(KeyHeader);
        _http.DefaultRequestHeaders.Add(KeyHeader, options.Value.Key);
    }

    public async Task<IReadOnlyList<SyncAlert>> GetAlertsAsync(SyncCursor? after, int limit, CancellationToken ct) =>
        await GetAsync<SyncAlert>(Url("/api/internal/sync/alerts", "updatedSince", after, limit), ct);

    public async Task<IReadOnlyList<SyncEventEnvelope>> GetEventsAsync(SyncCursor? after, int limit, CancellationToken ct) =>
        await GetAsync<SyncEventEnvelope>(Url("/api/internal/sync/events", "receivedAfter", after, limit), ct);

    internal static string Url(string path, string sinceName, SyncCursor? after, int limit)
    {
        var url = $"{path}?limit={limit.ToString(CultureInfo.InvariantCulture)}";
        if (after is not null)
        {
            url += $"&{sinceName}={Uri.EscapeDataString(after.At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))}"
                 + $"&afterId={Uri.EscapeDataString(after.Id.ToString())}";
        }
        return url;
    }

    private async Task<IReadOnlyList<T>> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<T>>(ct) ?? [];
    }
}
