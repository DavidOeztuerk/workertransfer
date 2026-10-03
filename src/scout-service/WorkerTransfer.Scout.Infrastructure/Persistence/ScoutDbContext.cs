using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>A row of <c>searches</c> — a saved question.</summary>
/// <remarks>
/// <para><strong>What does NOT stand here is the content of this table.</strong>
/// No hit, no identifier of a human being who was found, no time of a run, no
/// number. A saved result about people goes stale against a revocation, and a
/// revocation has to take effect at the next call (ADR-0013, ADR-0036
/// decision 4).</para>
///
/// <para>The words lie as <c>jsonb</c> in one column and not in a second table:
/// they are always read completely and replaced completely, there is no query
/// over a single word, and a table of their own would only bring a join and an
/// order nobody needs.</para>
/// </remarks>
public sealed class SearchRow
{
    /// <summary>Which search.</summary>
    public Guid Id { get; set; }

    /// <summary>For which company.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Who saved it — this service's person column.
    /// </summary>
    /// <remarks>
    /// By it <c>ErasureRecipientTests</c> recognises this service as an erasure
    /// recipient (ADR-0027 §4). From this one column on, <c>"scout"</c> belongs
    /// in <c>ErasureRecipient.Services</c>.
    /// </remarks>
    public Guid SubjectId { get; set; }

    /// <summary>What the person called it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The searched words, as jsonb.</summary>
    public List<string> Skills { get; set; } = [];

    /// <summary>Partial text on the location.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Only whoever expressly ticked remote.</summary>
    public bool Remote { get; set; }

    /// <summary>When it was saved.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>This service's one table of its own — and the outbox.</summary>
/// <remarks>
/// <para>Tracking is off, as the context's default and not per query. Per query
/// it would be discipline, and the next one added forgets it. A changing store
/// method therefore takes its <c>AsTracking()</c> expressly.</para>
///
/// <para><strong>No profile table, no hit table, no table "who looked at
/// whom".</strong> The first would be a copy of foreign data (ADR-0004), the
/// second the forbidden cache (ADR-0013), the third the most sensitive table of
/// the system (ADR-0033).</para>
/// </remarks>
public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : DbContext(options)
{
    /// <summary>The audit trail. Survives the erasure it records.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    /// <summary>The saved questions.</summary>
    public DbSet<SearchRow> Searches => Set<SearchRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SearchRow>(row =>
        {
            row.ToTable("searches");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id").IsRequired();
            row.Property(entry => entry.SubjectId).HasColumnName("subject_id").IsRequired();
            row.Property(entry => entry.Name)
                .HasColumnName("name").HasMaxLength(80).IsRequired();
            row.Property(entry => entry.Skills)
                .HasColumnName("skills").HasColumnType("jsonb").IsRequired();
            row.Property(entry => entry.Location)
                .HasColumnName("location").HasMaxLength(120).IsRequired();
            row.Property(entry => entry.Remote).HasColumnName("remote").IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();

            // The one query there is: the searches of ONE human being in ONE
            // company.
            row.HasIndex(entry => new { entry.TenantId, entry.SubjectId });
        });

        modelBuilder.ConfigureOutbox();

        modelBuilder.Entity<AuditRow>(row =>
        {
            row.ToTable("audit_events");
            row.HasKey(column => column.Id);
            row.Property(column => column.Id).HasColumnName("id");
            row.Property(column => column.Action).HasColumnName("action");
            row.Property(column => column.TargetId).HasColumnName("target_id");
            row.Property(column => column.OccurredAt).HasColumnName("occurred_at");
            row.HasIndex(column => new { column.TargetId, column.Action })
                .HasDatabaseName("ix_audit_events_target_action");

            // Expressly NO foreign key to the erased rows: the erasure removes
            // them and keeps the trail. A foreign key would mean either cascade
            // — then the proof would disappear with them — or prohibition, and
            // then the erasure would not go through.
        });

        base.OnModelCreating(modelBuilder);
    }
}
