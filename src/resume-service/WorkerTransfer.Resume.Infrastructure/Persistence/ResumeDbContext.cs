using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Domain.Audit;
using WorkerTransfer.Resume.Domain.Documents;
using WorkerTransfer.Resume.Domain.Requests;
using WorkerTransfer.Resume.Domain.Resumes;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>One row of <c>resumes</c>.</summary>
/// <remarks>
/// Not the aggregate. <see cref="EfResumeStore"/> builds a
/// <c>CurriculumVitae</c> from it, so nothing done to the aggregate can reach the
/// table by itself.
/// <para>
/// Positions and education are jsonb in this very row rather than child tables:
/// they are only ever read as a whole and written as a whole, there is no query
/// over a single position, and the aggregate can only guarantee its invariants
/// — at most one running position, the ordering — if it passes through the
/// domain complete. Two child tables would buy joins and partial updates for an
/// access pattern that does not exist.
/// </para>
/// </remarks>
public sealed class ResumeRow
{
    /// <summary>The subject id. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    /// <summary>The <c>positions</c> jsonb column, as written.</summary>
    public string Positions { get; set; } = "[]";

    /// <summary>The <c>education</c> jsonb column, as written.</summary>
    public string Education { get; set; } = "[]";

    /// <summary>The template the résumé is typeset in.</summary>
    public ResumeTemplate Template { get; set; } = ResumeTemplate.Plain;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>One row of <c>resume_documents</c>.</summary>
/// <remarks>
/// <strong>Without bytes.</strong> The content lies in the document store
/// (ADR-0035); here stands only where. A database holding files travels in full
/// into every backup and into every dump somebody ever pulls for debugging.
/// </remarks>
public sealed class DocumentRecordRow
{
    public Guid Id { get; set; }

    /// <summary>Whom it belongs to.</summary>
    public Guid SubjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DocumentKind Kind { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    /// <summary>The key in the document store.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>One row of <c>resume_document_terms</c>.</summary>
/// <remarks>
/// <strong>The index from PBI-7 — names, never the wording.</strong> What was
/// read in a document stands here as a list of canonical names; the full text
/// found is stored nowhere. A column with the wording of a reference would be a
/// second copy of the document, this time in the database — and therefore in
/// every backup (ADR-0043).
/// <para>
/// The row carries <c>subject_id</c> so the erasure finds it, and
/// <c>document_id</c> so it falls with its file. Both paths are measured, not
/// claimed.
/// </para>
/// </remarks>
public sealed class TermRow
{
    public Guid Id { get; set; }

    /// <summary>Whose document was read.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Which document.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Was there any text to read at all?</summary>
    public bool HasText { get; set; }

    /// <summary>The names found, as a <c>jsonb</c> field.</summary>
    /// <remarks>
    /// A table of its own per word would be the obvious normal form and the
    /// wrong one here: it could be searched by the word, and exactly that this
    /// index must never be able to do (ADR-0033). As a field in the document's
    /// row the only sensible question is the one about the document.
    /// </remarks>
    public string Terms { get; set; } = "[]";

    /// <summary>When it was read.</summary>
    public DateTime ReadAt { get; set; }
}

/// <summary>One row of <c>resume_requests</c>.</summary>
public sealed class RequestRow
{
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>
    /// <c>null</c> means the person who asked deleted their own account.
    /// </summary>
    /// <remarks>
    /// The request stays — it belongs to the company and is about a third
    /// person (ADR-0027 §2). It does <em>not</em> mean "nobody asked".
    /// </remarks>
    public Guid? RequestedBy { get; set; }

    public RequestState Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? AnsweredAt { get; set; }
}

/// <summary>One row of <c>audit_events</c>.</summary>
/// <remarks>
/// No metadata column. There is nothing technical to record here beyond who,
/// what, about whom and when — and a column that is always empty is a column
/// somebody eventually fills.
/// </remarks>
public sealed class AuditRow
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public Guid? TenantId { get; set; }

    public AuditAction Action { get; set; }

    public Guid? TargetId { get; set; }

    public string? CorrelationId { get; set; }

    public DateTime OccurredAt { get; set; }
}

/// <summary>The tables this service owns.</summary>
/// <remarks>
/// Every one of them is created by a migration in this project. There is no
/// shared database and no shared outbox (ADR-0004/0025) — the outbox hangs into
/// <em>this</em> context so the intent commits with the change that caused it.
/// <para>
/// Tracking is off for the whole context rather than per query. Per query it is
/// discipline, and the next query somebody adds forgets it; the repositories
/// run stored values back through the domain, which normalises them, so a
/// tracked read would mark rows modified and the next save would rewrite rows
/// nobody touched.
/// </para>
/// </remarks>
public sealed class ResumeDbContext(DbContextOptions<ResumeDbContext> options) : DbContext(options)
{
    /// <summary>The résumés.</summary>
    public DbSet<ResumeRow> Resumes => Set<ResumeRow>();

    /// <summary>The requests.</summary>
    public DbSet<RequestRow> Requests => Set<RequestRow>();

    /// <summary>The enclosed documents.</summary>
    public DbSet<DocumentRecordRow> Documents => Set<DocumentRecordRow>();

    /// <summary>What was read in them on request.</summary>
    public DbSet<TermRow> Terms => Set<TermRow>();

    /// <summary>The trail.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasPostgresEnum<RequestState>(name: EnumNames.RequestState);
        modelBuilder.HasPostgresEnum<DocumentKind>(name: EnumNames.DocumentKind);
        modelBuilder.HasPostgresEnum<ResumeTemplate>(name: EnumNames.ResumeTemplate);
        modelBuilder.HasPostgresEnum<AuditAction>(name: EnumNames.AuditAction);

        modelBuilder.Entity<ResumeRow>(entity =>
        {
            entity.ToTable("resumes");
            // NO second column for the person. The key IS the person, and
            // `resume_documents` and `resume_requests` beside it carry a
            // `subject_id` of their own — whoever compares the two takes this
            // for forgotten and adds the column. Then two statements about the
            // same human being would stand side by side, and the erasure
            // (ADR-0027) would hit the one and leave the other standing. The
            // annotation says it instead: it is what `LoeschempfaengerTests`
            // reads where there is no column name to find.
            entity.HasAnnotation(SubjectKeyedTable.Annotation, true);
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Positions)
                .HasColumnName("positions").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Education)
                .HasColumnName("education").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Template)
                .HasColumnName("template").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<DocumentRecordRow>(entity =>
        {
            entity.ToTable("resume_documents");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.HasIndex(row => row.SubjectId);
            entity.Property(row => row.Name)
                .HasColumnName("name").HasMaxLength(DocumentRecord.MaxNameLength).IsRequired();
            entity.Property(row => row.Kind).HasColumnName("kind").IsRequired();
            entity.Property(row => row.ContentType)
                .HasColumnName("content_type").HasMaxLength(64).IsRequired();
            entity.Property(row => row.SizeBytes).HasColumnName("size_bytes").IsRequired();
            entity.Property(row => row.StorageKey)
                .HasColumnName("storage_key").HasMaxLength(128).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
        });

        modelBuilder.Entity<TermRow>(entity =>
        {
            entity.ToTable("resume_document_terms");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(row => row.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(row => row.HasText).HasColumnName("has_text").IsRequired();
            entity.Property(row => row.Terms)
                .HasColumnName("terms").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.ReadAt).HasColumnName("read_at").IsRequired();

            entity.HasIndex(row => row.SubjectId);

            // ONE ROW PER DOCUMENT. The button reads everything anew every
            // time, and without this promise in the DATABASE two simultaneous
            // clicks would pile up two sets of terms for the same file — the
            // interface would then show the same suggestion twice, and which one
            // is the older nobody would know.
            entity.HasIndex(row => new { row.SubjectId, row.DocumentId })
                .IsUnique()
                .HasDatabaseName("uq_terms_subject_document");
        });

        modelBuilder.Entity<RequestRow>(entity =>
        {
            entity.ToTable("resume_requests");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(row => row.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(row => row.RequestedBy).HasColumnName("requested_by");
            entity.Property(row => row.Status).HasColumnName("status").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.AnsweredAt).HasColumnName("answered_at");

            entity.HasIndex(row => row.SubjectId);
            entity.HasIndex(row => row.TenantId);

            // "Ask once" — in the database and not only in the handler, because
            // two simultaneous requests would both pass a check in code. A
            // refusal is worthless if the same company may ask again.
            entity.HasIndex(row => new { row.SubjectId, row.TenantId })
                .IsUnique()
                .HasDatabaseName("uq_request_subject_tenant");
        });

        modelBuilder.Entity<AuditRow>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.ActorId).HasColumnName("actor_id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
            entity.Property(row => row.Action).HasColumnName("action").IsRequired();
            entity.Property(row => row.TargetId).HasColumnName("target_id");
            entity.Property(row => row.CorrelationId)
                .HasColumnName("correlation_id").HasMaxLength(120);
            entity.Property(row => row.OccurredAt).HasColumnName("occurred_at").IsRequired();

            entity.HasIndex(row => row.TargetId);
            entity.HasIndex(row => row.ActorId);
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>The names of the Postgres enums this service creates.</summary>
/// <remarks>
/// Named here rather than left to Npgsql's translator, because the same names
/// have to be given in three places — the model, the data source and the
/// options — and a default that changes would change all three silently. The
/// string values are Postgres type names and stay exactly as they are.
/// </remarks>
public static class EnumNames
{
    /// <summary>Behind <c>resume_requests.status</c>.</summary>
    public const string RequestState = "request_status";

    /// <summary>Behind <c>resume_documents.kind</c>.</summary>
    public const string DocumentKind = "document_kind";

    /// <summary>Behind <c>resumes.template</c>.</summary>
    public const string ResumeTemplate = "resume_template";

    /// <summary>Behind <c>audit_events.action</c>.</summary>
    public const string AuditAction = "audit_action";
}
