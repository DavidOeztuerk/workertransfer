using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Domain.Cases;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>One row of <c>market_status</c>.</summary>
public sealed class MarketRow
{
    /// <summary>The subject id. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    public string Availability { get; set; } = string.Empty;

    public bool Employed { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>One row of <c>market_requests</c>.</summary>
/// <remarks>
/// No <c>revoked_at</c>: the revocation lives in the ledger. Mirroring it here
/// would mean keeping two truths about the same question.
/// </remarks>
public sealed class RequestRow
{
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary><c>null</c> means: whoever asked has erased their account.</summary>
    /// <remarks>
    /// The request stays — it belongs to the company and is about a third party
    /// (ADR-0027 §2). It does <strong>not</strong> mean "nobody asked".
    /// </remarks>
    public Guid? RequestedBy { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? AnsweredAt { get; set; }
}

/// <summary>One row of <c>transfers</c>.</summary>
public sealed class CaseRow
{
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public Guid TenantId { get; set; }

    public string Status { get; set; } = string.Empty;

    public bool RequiresRelease { get; set; }

    public bool ReleaseConfirmed { get; set; }

    public string Message { get; set; } = string.Empty;

    public string OfferNote { get; set; } = string.Empty;

    public string? OfferStartOn { get; set; }

    public long? OfferFeeCents { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>The three tables this service owns — and its outbox.</summary>
/// <remarks>
/// There is no shared database and therefore no shared outbox either
/// (ADR-0004/0025) — it hangs in <em>this</em> context, so the intent commits
/// with the change that triggered it.
/// </remarks>
public sealed class TransferDbContext(DbContextOptions<TransferDbContext> options)
    : DbContext(options)
{
    /// <summary>The audit trail. Survives the erasure it records.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    /// <summary>The market statuses.</summary>
    public DbSet<MarketRow> MarketStatus => Set<MarketRow>();

    /// <summary>The market requests.</summary>
    public DbSet<RequestRow> Requests => Set<RequestRow>();

    /// <summary>The cases.</summary>
    public DbSet<CaseRow> Cases => Set<CaseRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<MarketRow>(entity =>
        {
            entity.ToTable("market_status");
            // NO second column for the person. The key IS the person, and
            // `market_requests` and `transfers` carry a `subject_id` of their
            // own beside it — whoever compares the two takes this for an
            // oversight and adds the column. Then two statements about the same
            // human being would stand side by side, and the erasure (ADR-0027)
            // would hit the one and leave the other. The annotation says it
            // instead: it is what `ErasureRecipientTests` reads where no column
            // name is to be found.
            entity.HasAnnotation(SubjectKeyedTable.Annotation, true);
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Availability)
                .HasColumnName("availability").HasMaxLength(16).IsRequired();
            entity.Property(row => row.Employed).HasColumnName("employed").IsRequired();
            entity.Property(row => row.Note)
                .HasColumnName("note").HasColumnType("text").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<RequestRow>(entity =>
        {
            entity.ToTable("market_requests");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(row => row.RequestedBy).HasColumnName("requested_by");
            entity.Property(row => row.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.AnsweredAt).HasColumnName("answered_at");

            entity.HasIndex(row => row.SubjectId);
            entity.HasIndex(row => row.TenantId);

            // Ask once — in the database and not only in the handler, because
            // two concurrent requests would both get through a check in code,
            // and a refusal would have no effect.
            entity.HasIndex(row => new { row.SubjectId, row.TenantId })
                .IsUnique()
                .HasDatabaseName("uq_market_requests_subject_tenant");
        });

        modelBuilder.Entity<CaseRow>(entity =>
        {
            entity.ToTable("transfers");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(row => row.RequiresRelease)
                .HasColumnName("requires_release").IsRequired();
            entity.Property(row => row.ReleaseConfirmed)
                .HasColumnName("release_confirmed").IsRequired();
            entity.Property(row => row.Message)
                .HasColumnName("message").HasColumnType("text").IsRequired();
            entity.Property(row => row.OfferNote)
                .HasColumnName("offer_note").HasColumnType("text").IsRequired();
            entity.Property(row => row.OfferStartOn)
                .HasColumnName("offer_start_on").HasMaxLength(7);
            entity.Property(row => row.OfferFeeCents)
                .HasColumnName("offer_fee_cents").HasColumnType("bigint");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(row => row.SubjectId);
            entity.HasIndex(row => row.TenantId);

            // Exactly ONE running case per (person, company). A second one
            // would be following up past the refusal. A partial index, because
            // completed cases may stand side by side any number of times.
            entity.HasIndex(row => new { row.SubjectId, row.TenantId })
                .IsUnique()
                .HasDatabaseName("uq_running_transfer")
                .HasFilter(RunningFilter());
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

    /// <summary>The partial-index filter, from the same set as the aggregate.</summary>
    /// <remarks>
    /// Built from <see cref="TransferStates.Running"/> rather than copied here:
    /// two lists that mean the same thing drift apart — and the database would
    /// be the one that does not say so.
    /// </remarks>
    private static string RunningFilter() =>
        "status IN ("
        + string.Join(", ", TransferStates.Running.Select(
            state => $"'{TransferStates.Word(state)}'"))
        + ")";
}
