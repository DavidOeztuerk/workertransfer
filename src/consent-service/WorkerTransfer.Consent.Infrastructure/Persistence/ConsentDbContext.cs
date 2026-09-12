using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorkerTransfer.Consent.Domain.Audit;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Infrastructure.Persistence;

/// <summary>One row of <c>consent_events</c>.</summary>
/// <remarks>
/// Not the fact itself. <see cref="EfConsentLedger"/> builds a
/// <see cref="ConsentEvent"/> from it, so nothing done to a fact can reach the
/// table by accident — and so a row that no longer satisfies the rules is
/// noticed on the way out.
/// <para>
/// No <c>updated_at</c>, and that absence is the point rather than an
/// oversight: nothing updates a row here.
/// </para>
/// </remarks>
public sealed class ConsentEventRow
{
    /// <summary>A monotonic surrogate, so the append order is readable at a glance.</summary>
    public long Id { get; set; }

    /// <summary>The identity of the fact, and what makes a re-delivery harmless.</summary>
    public Guid EventId { get; set; }

    public Guid SubjectId { get; set; }

    public string Capability { get; set; } = string.Empty;

    public ConsentAction Action { get; set; }

    public Guid? ActorId { get; set; }

    /// <summary>Free text. Cleared by an erasure, never by anything else.</summary>
    public string? Reason { get; set; }

    /// <summary>The <c>metadata</c> jsonb column, unparsed.</summary>
    public string Metadata { get; set; } = "{}";

    public DateTime RecordedAt { get; set; }
}

/// <summary>One row of <c>audit_events</c>.</summary>
public sealed class AuditEventRow
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public Guid? TenantId { get; set; }

    public AuditAction Action { get; set; }

    public Guid? TargetId { get; set; }

    public string? CorrelationId { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>The <c>metadata</c> jsonb column, unparsed.</summary>
    public string Metadata { get; set; } = "{}";
}

/// <summary>The two tables this service owns.</summary>
/// <remarks>
/// Tracking is off for the whole context rather than per query. Per query it is
/// discipline, and the next query somebody adds forgets it — while the ledger
/// runs stored values back through the domain, which would mark rows modified
/// and rewrite rows nobody touched.
/// <para>
/// The schema belongs to this service and is created by its own migrations.
/// There is no shared database and no second writer.
/// </para>
/// </remarks>
public sealed class ConsentDbContext(DbContextOptions<ConsentDbContext> options) : DbContext(options)
{
    /// <summary>The append-only log.</summary>
    public DbSet<ConsentEventRow> ConsentEvents => Set<ConsentEventRow>();

    /// <summary>The trail beside it.</summary>
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    /// <summary>
    /// Both action columns are text with a check constraint, not a Postgres
    /// enum.
    /// </summary>
    /// <remarks>
    /// Adding an action later is then a constraint change rather than an
    /// <c>ALTER TYPE</c> that locks the table — and the value in the column is
    /// the one that appears on the wire, so a row can be read without the
    /// mapping beside it.
    /// </remarks>
    private static readonly ValueConverter<ConsentAction, string> Handlungswert = new(
        wert => wert == ConsentAction.Grant
            ? "GRANT"
            : wert == ConsentAction.Revoke ? "REVOKE" : "DELETE",
        text => text == "GRANT"
            ? ConsentAction.Grant
            : text == "REVOKE" ? ConsentAction.Revoke : ConsentAction.Delete);

    private static readonly ValueConverter<AuditAction, string> Pruefhandlungswert = new(
        wert => wert == AuditAction.ConsentGrant
            ? "consent_grant"
            : wert == AuditAction.ConsentRevoke ? "consent_revoke" : "consent_delete",
        text => text == "consent_grant"
            ? AuditAction.ConsentGrant
            : text == "consent_revoke" ? AuditAction.ConsentRevoke : AuditAction.ConsentDelete);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ConsentEventRow>(entity =>
        {
            entity.ToTable("consent_events", tabelle => tabelle.HasCheckConstraint(
                "ck_consent_events_action", "action IN ('GRANT','REVOKE','DELETE')"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(row => row.EventId).HasColumnName("event_id");
            entity.Property(row => row.SubjectId).HasColumnName("subject_id");
            entity.Property(row => row.Capability).HasColumnName("capability");
            entity.Property(row => row.Action).HasColumnName("action")
                .HasConversion(Handlungswert).HasMaxLength(16);
            entity.Property(row => row.ActorId).HasColumnName("actor_id");
            entity.Property(row => row.Reason).HasColumnName("reason");
            entity.Property(row => row.Metadata).HasColumnName("metadata")
                .HasColumnType("jsonb").HasDefaultValue("{}");
            entity.Property(row => row.RecordedAt).HasColumnName("recorded_at");

            // One fact, one row: a re-delivered erasure or a retried write
            // cannot append the same fact twice.
            entity.HasIndex(row => row.EventId).IsUnique().HasDatabaseName("ux_consent_events_event");

            // Serves the reduction verbatim — filter by (subject, capability),
            // take the newest row, tie-break on event_id.
            entity.HasIndex(row => new { row.SubjectId, row.Capability, row.RecordedAt, row.EventId })
                .HasDatabaseName("ix_consent_events_lookup")
                .IsDescending(false, false, true, true);
        });

        modelBuilder.Entity<AuditEventRow>(entity =>
        {
            entity.ToTable("audit_events", tabelle => tabelle.HasCheckConstraint(
                "ck_audit_events_action",
                "action IN ('consent_grant','consent_revoke','consent_delete')"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.ActorId).HasColumnName("actor_id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
            entity.Property(row => row.Action).HasColumnName("action")
                .HasConversion(Pruefhandlungswert).HasMaxLength(32);
            entity.Property(row => row.TargetId).HasColumnName("target_id");
            entity.Property(row => row.CorrelationId).HasColumnName("correlation_id")
                .HasMaxLength(64);
            entity.Property(row => row.OccurredAt).HasColumnName("occurred_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.Metadata).HasColumnName("metadata")
                .HasColumnType("jsonb").HasDefaultValue("{}");

            entity.HasIndex(row => row.ActorId).HasDatabaseName("ix_audit_events_actor");
            entity.HasIndex(row => row.TargetId).HasDatabaseName("ix_audit_events_target");
            entity.HasIndex(row => row.TenantId).HasDatabaseName("ix_audit_events_tenant");
        });
    }
}
