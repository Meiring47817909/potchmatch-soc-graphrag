using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SocService.Ingestion;

namespace SocService.Tests;

public class EventIngestionServiceTests
{
    private static List<SerilogEnvelope> Valid(int n) =>
        Enumerable.Range(0, n).Select(_ => EnvelopeBuilder.Make()).ToList();

    [Fact]
    public async Task Ingest_StoresValidBatch()
    {
        using var db = TestDb.Create();
        var svc = new EventIngestionService(db, NullLogger<EventIngestionService>.Instance);
        var result = await svc.IngestAsync(Valid(3), CancellationToken.None);
        Assert.Equal(new IngestResult(3, 0), result);
        Assert.Equal(3, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Ingest_SameBatchTwice_StoredOnce()
    {
        using var db = TestDb.Create();
        var svc = new EventIngestionService(db, NullLogger<EventIngestionService>.Instance);
        var batch = Valid(3);
        await svc.IngestAsync(batch, CancellationToken.None);
        var second = await svc.IngestAsync(batch, CancellationToken.None);
        Assert.Equal(new IngestResult(3, 0), second);
        Assert.Equal(3, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Ingest_DuplicateWithinBatch_StoredOnce()
    {
        using var db = TestDb.Create();
        var svc = new EventIngestionService(db, NullLogger<EventIngestionService>.Instance);
        var env = EnvelopeBuilder.Make();
        var result = await svc.IngestAsync(new List<SerilogEnvelope> { env, env }, CancellationToken.None);
        Assert.Equal(new IngestResult(2, 0), result);
        Assert.Equal(1, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Ingest_MixedBatch_StoresValid_CountsRejected()
    {
        using var db = TestDb.Create();
        var svc = new EventIngestionService(db, NullLogger<EventIngestionService>.Instance);
        var batch = Valid(2);
        batch.Add(EnvelopeBuilder.Make(overrides: new() { ["Severity"] = "bogus" }));
        var result = await svc.IngestAsync(batch, CancellationToken.None);
        Assert.Equal(new IngestResult(2, 1), result);
        Assert.Equal(2, await db.Events.CountAsync());
    }
}
