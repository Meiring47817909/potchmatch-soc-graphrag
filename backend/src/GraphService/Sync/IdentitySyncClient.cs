using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace GraphService.Sync;

public interface IIdentitySyncClient
{
    Task<IReadOnlyList<SyncPlayer>> GetPlayersAsync(CancellationToken ct);
}

public class IdentitySyncClient : IIdentitySyncClient
{
    private readonly HttpClient _http;

    public IdentitySyncClient(HttpClient http, IOptions<GraphSyncOptions> options)
    {
        _http = http;
        _http.DefaultRequestHeaders.Remove(SocSyncClient.KeyHeader);
        _http.DefaultRequestHeaders.Add(SocSyncClient.KeyHeader, options.Value.Key);
    }

    public async Task<IReadOnlyList<SyncPlayer>> GetPlayersAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("/api/internal/sync/players", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SyncPlayer>>(ct) ?? [];
    }
}
