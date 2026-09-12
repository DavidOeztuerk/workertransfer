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
/// <c>Lebenslauf</c> from it, so nothing done to the aggregate can reach the
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
public sealed class LebenslaufZeile
{
    /// <summary>The subject id. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    /// <summary>The <c>positions</c> jsonb column, as written.</summary>
    public string Positions { get; set; } = "[]";

    /// <summary>The <c>education</c> jsonb column, as written.</summary>
    public string Education { get; set; } = "[]";

    /// <summary>In welcher Vorlage der Lebenslauf gesetzt wird.</summary>
    public Vorlage Template { get; set; } = Vorlage.Schlicht;

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
public sealed class UnterlageZeile
{
    public Guid Id { get; set; }

    /// <summary>Wem sie gehoert.</summary>
    public Guid SubjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public Unterlagenart Kind { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    /// <summary>Der Schluessel in der Ablage.</summary>
    public string StorageKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

/// <summary>Eine Zeile von <c>resume_document_terms</c>.</summary>
/// <remarks>
/// <strong>Der Index aus PBI-7 — Namen, nie der Wortlaut.</strong> Was in einer
/// Unterlage gelesen wurde, steht hier als Liste kanonischer Namen; der
/// gefundene Volltext wird nirgends abgelegt. Eine Spalte mit dem Wortlaut
/// eines Zeugnisses waere eine zweite Kopie der Unterlage, diesmal in der
/// Datenbank — und damit in jeder Sicherung (ADR-0043).
/// <para>
/// Die Zeile traegt <c>subject_id</c>, damit die Loeschung sie findet, und
/// <c>document_id</c>, damit sie mit ihrer Datei faellt. Beide Wege sind
/// gemessen, nicht behauptet.
/// </para>
/// </remarks>
public sealed class FundZeile
{
    public Guid Id { get; set; }

    /// <summary>Wessen Unterlage gelesen wurde.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Welche Unterlage.</summary>
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
public sealed class AnfrageZeile
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

    public Anfragestand Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? AnsweredAt { get; set; }
}

/// <summary>One row of <c>audit_events</c>.</summary>
/// <remarks>
/// No metadata column. There is nothing technical to record here beyond who,
/// what, about whom and when — and a column that is always empty is a column
/// somebody eventually fills.
/// </remarks>
public sealed class PruefZeile
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public Guid? TenantId { get; set; }

    public Pruefhandlung Action { get; set; }

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
    public DbSet<LebenslaufZeile> Lebenslaeufe => Set<LebenslaufZeile>();

    /// <summary>The requests.</summary>
    public DbSet<AnfrageZeile> Anfragen => Set<AnfrageZeile>();

    /// <summary>Die beigelegten Unterlagen.</summary>
    public DbSet<UnterlageZeile> Unterlagen => Set<UnterlageZeile>();

    /// <summary>Was auf Ausloesung darin gelesen wurde.</summary>
    public DbSet<FundZeile> Funde => Set<FundZeile>();

    /// <summary>The trail.</summary>
    public DbSet<PruefZeile> Pruefspur => Set<PruefZeile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasPostgresEnum<Anfragestand>(name: Enumnamen.Anfragestand);
        modelBuilder.HasPostgresEnum<Unterlagenart>(name: Enumnamen.Unterlagenart);
        modelBuilder.HasPostgresEnum<Vorlage>(name: Enumnamen.Vorlage);
        modelBuilder.HasPostgresEnum<Pruefhandlung>(name: Enumnamen.Pruefhandlung);

        modelBuilder.Entity<LebenslaufZeile>(entity =>
        {
            entity.ToTable("resumes");
            // KEINE zweite Spalte fuer die Person. Der Schluessel IST sie, und
            // `resume_documents` und `resume_requests` fuehren daneben eine eigene
            // `subject_id` — wer die beiden vergleicht, haelt das hier fuer
            // vergessen und traegt die Spalte nach. Dann stuenden zwei Angaben
            // ueber denselben Menschen nebeneinander, und die Loeschung (ADR-0027)
            // traefe die eine und liesse die andere stehen. Die Anmerkung sagt es
            // stattdessen aus: sie ist das, was `LoeschempfaengerTests` liest, wo
            // kein Spaltenname zu finden ist.
            entity.HasAnnotation(Personenzeile.Anmerkung, true);
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.Positions)
                .HasColumnName("positions").HasColumnType("jsonb").IsRequired();
            entity.Property(zeile => zeile.Education)
                .HasColumnName("education").HasColumnType("jsonb").IsRequired();
            entity.Property(zeile => zeile.Template)
                .HasColumnName("template").IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<UnterlageZeile>(entity =>
        {
            entity.ToTable("resume_documents");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.HasIndex(zeile => zeile.SubjectId);
            entity.Property(zeile => zeile.Name)
                .HasColumnName("name").HasMaxLength(Unterlage.HoechstlaengeName).IsRequired();
            entity.Property(zeile => zeile.Kind).HasColumnName("kind").IsRequired();
            entity.Property(zeile => zeile.ContentType)
                .HasColumnName("content_type").HasMaxLength(64).IsRequired();
            entity.Property(zeile => zeile.SizeBytes).HasColumnName("size_bytes").IsRequired();
            entity.Property(zeile => zeile.StorageKey)
                .HasColumnName("storage_key").HasMaxLength(128).IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
        });

        modelBuilder.Entity<FundZeile>(entity =>
        {
            entity.ToTable("resume_document_terms");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(zeile => zeile.DocumentId).HasColumnName("document_id").IsRequired();
            entity.Property(zeile => zeile.HasText).HasColumnName("has_text").IsRequired();
            entity.Property(zeile => zeile.Terms)
                .HasColumnName("terms").HasColumnType("jsonb").IsRequired();
            entity.Property(zeile => zeile.ReadAt).HasColumnName("read_at").IsRequired();

            entity.HasIndex(zeile => zeile.SubjectId);

            // EINE ZEILE JE UNTERLAGE. Der Knopf liest jedes Mal alles neu, und
            // ohne diese Zusage in der DATENBANK haeuften zwei gleichzeitige
            // Klicks zwei Funde zur selben Datei an — die Oberfläche zeigte
            // dann denselben Vorschlag doppelt, und welcher der aeltere ist,
            // wuesste niemand.
            entity.HasIndex(zeile => new { zeile.SubjectId, zeile.DocumentId })
                .IsUnique()
                .HasDatabaseName("uq_terms_subject_document");
        });

        modelBuilder.Entity<AnfrageZeile>(entity =>
        {
            entity.ToTable("resume_requests");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(zeile => zeile.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(zeile => zeile.RequestedBy).HasColumnName("requested_by");
            entity.Property(zeile => zeile.Status).HasColumnName("status").IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.AnsweredAt).HasColumnName("answered_at");

            entity.HasIndex(zeile => zeile.SubjectId);
            entity.HasIndex(zeile => zeile.TenantId);

            // "Ask once" — in the database and not only in the handler, because
            // two simultaneous requests would both pass a check in code. A
            // refusal is worthless if the same company may ask again.
            entity.HasIndex(zeile => new { zeile.SubjectId, zeile.TenantId })
                .IsUnique()
                .HasDatabaseName("uq_request_subject_tenant");
        });

        modelBuilder.Entity<PruefZeile>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.ActorId).HasColumnName("actor_id");
            entity.Property(zeile => zeile.TenantId).HasColumnName("tenant_id");
            entity.Property(zeile => zeile.Action).HasColumnName("action").IsRequired();
            entity.Property(zeile => zeile.TargetId).HasColumnName("target_id");
            entity.Property(zeile => zeile.CorrelationId)
                .HasColumnName("correlation_id").HasMaxLength(120);
            entity.Property(zeile => zeile.OccurredAt).HasColumnName("occurred_at").IsRequired();

            entity.HasIndex(zeile => zeile.TargetId);
            entity.HasIndex(zeile => zeile.ActorId);
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
    public const string Anfragestand = "request_status";

    /// <summary>Hinter <c>resume_documents.kind</c>.</summary>
    public const string Unterlagenart = "document_kind";

    /// <summary>Hinter <c>resumes.template</c>.</summary>
    public const string Vorlage = "resume_template";

    /// <summary>Behind <c>audit_events.action</c>.</summary>
    public const string Pruefhandlung = "audit_action";
}
