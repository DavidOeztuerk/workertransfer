using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>One row of <c>resumes</c>.</summary>
/// <remarks>
/// Not the aggregate. <see cref="EfLebenslaufSpeicher"/> builds a
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

    /// <summary>In welcher ResumeTemplate der CurriculumVitae gesetzt wird.</summary>
    public ResumeTemplate Template { get; set; } = ResumeTemplate.Plain;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Eine Zeile von <c>resume_documents</c>.</summary>
/// <remarks>
/// <strong>Ohne Bytes.</strong> Der Inhalt liegt in der Ablage (ADR-0035);
/// hier steht nur, wo. Eine Datenbank, in der Dateien liegen, wandert
/// vollstaendig in jede Sicherung und in jeden Abzug, den irgendwer einmal
/// fuer eine Fehlersuche zieht.
/// </remarks>
public sealed class DocumentRecordRow
{
    public Guid Id { get; set; }

    /// <summary>Wem sie gehoert.</summary>
    public Guid SubjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DocumentKind Kind { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    /// <summary>Der Schluessel in der Ablage.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>Eine Zeile von <c>resume_document_terms</c>.</summary>
/// <remarks>
/// <strong>Der Index aus PBI-7 — Namen, nie der Wortlaut.</strong> Was in einer
/// DocumentRecord gelesen wurde, steht hier als Liste kanonischer Namen; der
/// gefundene Volltext wird nirgends abgelegt. Eine Spalte mit dem Wortlaut
/// eines Zeugnisses waere eine zweite Kopie der DocumentRecord, diesmal in der
/// Datenbank — und damit in jeder Sicherung (ADR-0043).
/// <para>
/// Die Zeile traegt <c>subject_id</c>, damit die Erasure sie findet, und
/// <c>document_id</c>, damit sie mit ihrer Datei faellt. Beide Wege sind
/// gemessen, nicht behauptet.
/// </para>
/// </remarks>
public sealed class TermRow
{
    public Guid Id { get; set; }

    /// <summary>Wessen DocumentRecord gelesen wurde.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Welche DocumentRecord.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>War ueberhaupt Text zu lesen?</summary>
    public bool HasText { get; set; }

    /// <summary>Die gefundenen Namen, als <c>jsonb</c>-Feld.</summary>
    /// <remarks>
    /// Eine eigene Tabelle je Wort waere die naheliegende Normalform und hier
    /// die falsche: sie liesse sich nach dem Wort durchsuchen, und genau das
    /// darf dieser Index nie koennen (ADR-0033). Als Feld in der Zeile des
    /// Dokuments ist die einzige sinnvolle Frage die nach dem Dokument.
    /// </remarks>
    public string Terms { get; set; } = "[]";

    /// <summary>Wann gelesen wurde.</summary>
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
    public DbSet<ResumeRow> Lebenslaeufe => Set<ResumeRow>();

    /// <summary>The requests.</summary>
    public DbSet<RequestRow> Anfragen => Set<RequestRow>();

    /// <summary>Die beigelegten Unterlagen.</summary>
    public DbSet<DocumentRecordRow> Unterlagen => Set<DocumentRecordRow>();

    /// <summary>Was auf Ausloesung darin gelesen wurde.</summary>
    public DbSet<TermRow> Funde => Set<TermRow>();

    /// <summary>The trail.</summary>
    public DbSet<AuditRow> Pruefspur => Set<AuditRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasPostgresEnum<RequestState>(name: Enumnamen.RequestState);
        modelBuilder.HasPostgresEnum<DocumentKind>(name: Enumnamen.DocumentKind);
        modelBuilder.HasPostgresEnum<ResumeTemplate>(name: Enumnamen.ResumeTemplate);
        modelBuilder.HasPostgresEnum<AuditAction>(name: Enumnamen.AuditAction);

        modelBuilder.Entity<ResumeRow>(entity =>
        {
            entity.ToTable("resumes");
            // KEINE zweite Spalte fuer die Person. Der Schluessel IST sie, und
            // `resume_documents` und `resume_requests` fuehren daneben eine eigene
            // `subject_id` — wer die beiden vergleicht, haelt das hier fuer
            // vergessen und traegt die Spalte nach. Dann stuenden zwei Angaben
            // ueber denselben Menschen nebeneinander, und die Erasure (ADR-0027)
            // traefe die eine und liesse die andere stehen. Die Anmerkung sagt es
            // stattdessen aus: sie ist das, was `LoeschempfaengerTests` liest, wo
            // kein Spaltenname zu finden ist.
            entity.HasAnnotation(SubjectKeyedTable.Anmerkung, true);
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
                .HasColumnName("name").HasMaxLength(DocumentRecord.HoechstlaengeName).IsRequired();
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

            // EINE ZEILE JE UNTERLAGE. Der Knopf liest jedes Mal alles neu, und
            // ohne diese Zusage in der DATENBANK haeuften zwei gleichzeitige
            // Klicks zwei Funde zur selben Datei an — die Oberfläche zeigte
            // dann denselben Vorschlag doppelt, und welcher der aeltere ist,
            // wuesste niemand.
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

/// <summary>The names of the two Postgres enums this service creates.</summary>
/// <remarks>
/// Named here rather than left to Npgsql's translator, because the same two
/// names have to be given in three places — the model, the data source and the
/// options — and a default that changes would change all three silently.
/// </remarks>
public static class Enumnamen
{
    /// <summary>Behind <c>resume_requests.status</c>.</summary>
    public const string RequestState = "request_status";

    /// <summary>Hinter <c>resume_documents.kind</c>.</summary>
    public const string DocumentKind = "document_kind";

    /// <summary>Hinter <c>resumes.template</c>.</summary>
    public const string ResumeTemplate = "resume_template";

    /// <summary>Behind <c>audit_events.action</c>.</summary>
    public const string AuditAction = "audit_action";
}
