using Microsoft.EntityFrameworkCore;
using SocService.Data;
using SocService.Models;

namespace SocService.Ingestion;

public record IngestResult(int Accepted, int Rejected);

public class EventIngestionService
{
    private readonly SocDbContext _db;
    private readonly ILogger<EventIngestionService> _logger;

    public EventIngestionService(SocDbContext db, ILogger<EventIngestionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IngestResult> IngestAsync(IReadOnlyList<SerilogEnvelope> batch, CancellationToken ct)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var mapped = new List<SocEvent>();
        var rejected = 0;

        foreach (var env in batch)
        {
            if (SerilogEventMapper.TryMap(env, receivedAt, out var evt, out var error))
            {
                mapped.Add(evt!);
            }
            else
            {
                rejected++;
                _logger.LogWarning("Rejected ingest event: {Reason}", error);
            }
        }

        // Duplicates (within the batch or already stored) count as accepted but are stored once.
        var ids = mapped.Select(e => e.EventId).Distinct().ToList();
        var existing = (await _db.Events.Where(e => ids.Contains(e.EventId))
            .Select(e => e.EventId).ToListAsync(ct)).ToHashSet();

        var seen = new HashSet<Guid>(existing);
        foreach (var evt in mapped)
        {
            if (seen.Add(evt.EventId))
                _db.Events.Add(evt);
        }

        await _db.SaveChangesAsync(ct);
        return new IngestResult(mapped.Count, rejected);
    }
}
