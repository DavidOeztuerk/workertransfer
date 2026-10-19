using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>A row of <c>applications</c>.</summary>
/// <remarks>
/// Not the aggregate. <see cref="EfApplicationStore"/> builds a
/// <c>JobApplication</c> from it, so nothing that happens to the aggregate lands
/// in the table on its own.
/// </remarks>
public sealed class ApplicationRow
{
    /// <summary>Which application.</summary>
    public Guid Id { get; set; }

    /// <summary>For which job.</summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Which company — copied from the job.
    /// </summary>
    /// <remarks>
    /// A foreign key is impossible: the jobs live in another database
    /// (ADR-0004). A copy is only dangerous when the original can change, and a
    /// job does not change its company.
    /// </remarks>
    public Guid TenantId { get; set; }

    /// <summary>Who applied.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>What the person wrote.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Whether the résumé went along.</summary>
    public bool SharesResume { get; set; }

    /// <summary>Whether the portfolio went along.</summary>
    public bool SharesPortfolio { get; set; }

    /// <summary>Which documents were enclosed, as a jsonb list of ids.</summary>
    public string Documents { get; set; } = "[]";

    /// <summary>The state, as a word.</summary>
    /// <remarks>
    /// As <c>varchar</c> and not as a Postgres enum: the state stands like this
    /// in the contract and in the interface too, and a type that needs a schema
    /// migration of its own for every extension buys nothing here.
    /// </remarks>
    public string Status { get; set; } = string.Empty;

    /// <summary>When it was sent.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When something last changed.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary><c>null</c> as long as nothing is decided.</summary>
    public DateTime? AnsweredAt { get; set; }

    /// <summary>
    /// Snapshot of the letterhead (ADR-0038). jsonb, because it is written and
    /// read as a whole and is never the search key.
    /// </summary>
    public string ApplicantContact { get; set; } = "{}";
}

/// <summary>A row of <c>application_drafts</c>.</summary>
/// <remarks>
/// The draft is the EARLIER LIFE of the same thing — it becomes the application
/// once a human being has clicked twice. That is why it lives in this service
/// and not in one of its own: it is the same subject matter, only one stage
/// before.
/// </remarks>
public sealed class DraftRow
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public Guid TenantId { get; set; }

    public Guid SubjectId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>The state, as a word — like the application's, for the same reason.</summary>
    public string Status { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    /// <summary>The KIND of a failure, never its content.</summary>
    public string Error { get; set; } = string.Empty;

    public bool SharesResume { get; set; }

    /// <summary>Which documents go along, as a jsonb list of ids.</summary>
    /// <remarks>
    /// A list and not a join table: it is always written as a whole and read as
    /// a whole, and one row per attachment would be the invitation to sort or
    /// count by it.
    /// </remarks>
    public string Documents { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Start of this writing. Null as soon as it is no longer Generating.</summary>
    public DateTime? WritingStartedAt { get; set; }
}

/// <summary>A row of <c>application_draft_comments</c>.</summary>
/// <remarks>
/// A table OF ITS OWN, unlike the attachments: comments come about one by one,
/// are ticked off one by one and read one by one. Keeping them as jsonb would
/// mean writing the whole list anew with every comment.
/// </remarks>
public sealed class CommentRow
{
    public Guid Id { get; set; }

    public Guid DraftId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>The marked passage, or empty.</summary>
    public string Quote { get; set; } = string.Empty;

    public bool Resolved { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>The tables this service owns.</summary>
/// <remarks>
/// There is no shared database and therefore no shared outbox either
/// (ADR-0004/0025) — it hangs in <em>this</em> context, so the intent commits
/// with the change that triggered it.
/// <para>
/// Tracking is off for the whole context instead of per query. Per query it is
/// discipline, and the next query somebody adds forgets it; the store leads read
/// values back through the domain anyway, which normalises them, so a tracked
/// read would mark rows as changed and the next commit would write back rows
/// nobody touched.
/// </para>
/// </remarks>
public sealed class ApplicationsDbContext(DbContextOptions<ApplicationsDbContext> options)
    : DbContext(options)
{
    /// <summary>The audit trail. Survives the erasure it records.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    /// <summary>The applications.</summary>
    public DbSet<ApplicationRow> Applications => Set<ApplicationRow>();

    /// <summary>The drafts.</summary>
    public DbSet<DraftRow> Drafts => Set<DraftRow>();

    /// <summary>The comments on the drafts.</summary>
    public DbSet<CommentRow> Comments => Set<CommentRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ApplicationRow>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.JobId).HasColumnName("job_id").IsRequired();
            entity.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(row => row.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(row => row.Message)
                .HasColumnName("message").HasColumnType("text").IsRequired();
            entity.Property(row => row.SharesResume)
                .HasColumnName("shares_resume").IsRequired();
            entity.Property(row => row.SharesPortfolio)
                .HasColumnName("shares_portfolio").IsRequired();
            entity.Property(row => row.Documents)
                .HasColumnName("documents").HasColumnType("jsonb")
                .HasDefaultValue("[]").IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(row => row.AnsweredAt).HasColumnName("answered_at");
            entity.Property(row => row.ApplicantContact)
                .HasColumnName("applicant_contact")
                .HasColumnType("jsonb")
                .HasDefaultValue("{}")
                .IsRequired();

            entity.HasIndex(row => row.JobId);
            entity.HasIndex(row => row.TenantId);
            entity.HasIndex(row => row.SubjectId);

            // Exactly one per (person, job) — in the database and not only in
            // the handler, because two simultaneous submissions would both get
            // through a check in code. Applying twice for the same job is no
            // expression of interest but an oversight.
            entity.HasIndex(row => new { row.JobId, row.SubjectId })
                .IsUnique()
                .HasDatabaseName("uq_application_job_subject");
        });

        modelBuilder.Entity<DraftRow>(entity =>
        {
            entity.ToTable("application_drafts");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.JobId).HasColumnName("job_id").IsRequired();
            entity.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(row => row.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(row => row.Subject)
                .HasColumnName("subject")
                .HasMaxLength(ApplicationDraft.MaxSubjectLineLength).IsRequired();
            entity.Property(row => row.Body)
                .HasColumnName("body").HasColumnType("text").IsRequired();
            entity.Property(row => row.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(row => row.Version).HasColumnName("version").IsRequired();
            entity.Property(row => row.Error)
                .HasColumnName("error").HasColumnType("text").IsRequired();
            entity.Property(row => row.SharesResume)
                .HasColumnName("shares_resume").IsRequired();
            entity.Property(row => row.Documents)
                .HasColumnName("documents").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(row => row.WritingStartedAt).HasColumnName("writing_started_at");

            entity.HasIndex(row => row.SubjectId);

            // NO unique index on (job, person): a sent draft stays as evidence,
            // and afterwards a new one may come about. The uniqueness sits on the
            // APPLICATION, where it belongs — applying twice is the oversight,
            // drafting twice is not.
        });

        modelBuilder.Entity<CommentRow>(entity =>
        {
            entity.ToTable("application_draft_comments");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.DraftId).HasColumnName("draft_id").IsRequired();
            entity.Property(row => row.Body)
                .HasColumnName("body")
                .HasMaxLength(Comment.MaxTextLength).IsRequired();
            entity.Property(row => row.Quote)
                .HasColumnName("quote")
                .HasMaxLength(Comment.MaxPassageLength).IsRequired();
            entity.Property(row => row.Resolved).HasColumnName("resolved").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(row => row.DraftId);
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
