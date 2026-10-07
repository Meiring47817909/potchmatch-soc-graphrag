using Microsoft.EntityFrameworkCore;
using SocService.Models;

namespace SocService.Data;

public class SocDbContext : DbContext
{
    public SocDbContext(DbContextOptions<SocDbContext> options) : base(options) { }

    public DbSet<SocEvent> Events => Set<SocEvent>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SocEvent>(e =>
        {
            e.ToTable("events");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasColumnName("event_id").ValueGeneratedNever();
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.ReceivedAt).HasColumnName("received_at");
            e.Property(x => x.ServiceName).HasColumnName("service_name");
            e.Property(x => x.EventType).HasColumnName("event_type");
            e.Property(x => x.Severity).HasColumnName("severity");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.SourceIp).HasColumnName("source_ip");
            e.Property(x => x.Endpoint).HasColumnName("endpoint");
            e.Property(x => x.HttpMethod).HasColumnName("http_method");
            e.Property(x => x.StatusCode).HasColumnName("status_code");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            e.Property(x => x.AffectedEntity).HasColumnName("affected_entity");

            e.HasIndex(x => new { x.EventType, x.Timestamp });
            e.HasIndex(x => new { x.SourceIp, x.Timestamp });
            e.HasIndex(x => new { x.UserId, x.Timestamp });
        });

        modelBuilder.Entity<Alert>(a =>
        {
            a.ToTable("alerts");
            a.HasKey(x => x.AlertId);
            a.Property(x => x.AlertId).HasColumnName("alert_id").ValueGeneratedNever();
            a.Property(x => x.Timestamp).HasColumnName("timestamp");
            a.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            a.Property(x => x.RuleName).HasColumnName("rule_name");
            a.Property(x => x.Severity).HasColumnName("severity");
            a.Property(x => x.Status).HasColumnName("status");
            a.Property(x => x.Description).HasColumnName("description");
            a.Property(x => x.AffectedService).HasColumnName("affected_service");
            a.Property(x => x.AffectedUser).HasColumnName("affected_user");
            a.Property(x => x.AffectedEntity).HasColumnName("affected_entity");
            a.Property(x => x.RecommendedAction).HasColumnName("recommended_action");
            a.Property(x => x.DedupKey).HasColumnName("dedup_key");

            a.HasIndex(x => new { x.DedupKey, x.Status });
            a.HasMany(x => x.AlertEvents).WithOne().HasForeignKey(x => x.AlertId);
        });

        modelBuilder.Entity<AlertEvent>(ae =>
        {
            ae.ToTable("alert_events");
            ae.HasKey(x => new { x.AlertId, x.EventId });
            ae.Property(x => x.AlertId).HasColumnName("alert_id");
            ae.Property(x => x.EventId).HasColumnName("event_id");
            ae.HasOne<SocEvent>().WithMany().HasForeignKey(x => x.EventId);
        });
    }
}
