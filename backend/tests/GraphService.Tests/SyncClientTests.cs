using System.Net;
using GraphService.Sync;
using Microsoft.Extensions.Options;

namespace GraphService.Tests;

public class SyncClientTests
{
    private sealed class Stub(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static SocSyncClient Client(Stub stub) => new(
        new HttpClient(stub) { BaseAddress = new Uri("http://soc") }, Options.Create(new GraphSyncOptions { Key = "k1" }));

    [Fact]
    public async Task Events_WithCursor_SendsEncodedUtcTimestampAndKeyHeader()
    {
        var stub = new Stub(HttpStatusCode.OK, "[]");
        var at = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(2));
        var id = Guid.NewGuid();
        await Client(stub).GetEventsAsync(new SyncCursor(at, id), 500, default);

        var q = stub.Last!.RequestUri!.PathAndQuery;
        Assert.Contains("receivedAfter=2026-10-08T10%3A00%3A00.0000000Z", q);
        Assert.Contains($"afterId={id}", q);
        Assert.Contains("limit=500", q);
        Assert.Equal("k1", stub.Last.Headers.GetValues("X-Graph-Sync-Key").Single());
    }

    [Fact]
    public async Task Alerts_NoCursor_SendsNoSinceOrAfterId_AndNon2xxThrows()
    {
        var ok = new Stub(HttpStatusCode.OK, "[]");
        await Client(ok).GetAlertsAsync(null, 200, default);
        Assert.DoesNotContain("updatedSince", ok.Last!.RequestUri!.Query);
        Assert.DoesNotContain("afterId", ok.Last.RequestUri.Query);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(new Stub(HttpStatusCode.Unauthorized, "")).GetAlertsAsync(null, 200, default));
    }
}
