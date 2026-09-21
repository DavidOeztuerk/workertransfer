using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>applications</c>.</summary>
/// <remarks>
/// Nicht das Aggregat. <see cref="EfBewerbungsspeicher"/> baut daraus eine
/// <c>JobApplication</c>, damit nichts, was am Aggregat geschieht, von allein in der
/// Tabelle landet.
/// </remarks>
public sealed class ApplicationRow
{
    /// <summary>Welche JobApplication.</summary>
    public Guid Id { get; set; }

    /// <summary>Auf welche Job.</summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Welches Unternehmen — aus der Job kopiert.
    /// </summary>
    /// <remarks>
    /// Ein Fremdschlüssel geht nicht: die Jobs liegen in einer anderen
    /// Datenbank (ADR-0004). Eine Kopie ist nur gefährlich, wenn das Original
    /// sich ändern kann, und eine Job wechselt nicht das Unternehmen.
    /// </remarks>
    public Guid TenantId { get; set; }

    /// <summary>Wer sich beworben hat.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Was die Person geschrieben hat.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Ob der CurriculumVitae mitging.</summary>
    public bool SharesResume { get; set; }

    /// <summary>Ob das Portfolio mitging.</summary>
    public bool SharesPortfolio { get; set; }

    /// <summary>Welche Unterlagen beilagen, als jsonb-Liste von Kennungen.</summary>
    public string Documents { get; set; } = "[]";

    /// <summary>Der State, als Wort.</summary>
    /// <remarks>
    /// Als <c>varchar</c> und nicht als Postgres-Enum: der State steht so auch
    /// im Vertrag und in der Oberfläche, und ein Typ, der bei jeder Erweiterung
    /// eine eigene SchemaMigration braucht, kauft hier nichts.
    /// </remarks>
    public string Status { get; set; } = string.Empty;

    /// <summary>Wann sie abgeschickt wurde.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Wann sich zuletzt etwas änderte.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary><c>null</c>, solange nicht entschieden.</summary>
    public DateTime? AnsweredAt { get; set; }

    /// <summary>
    /// Snapshot des Briefkopfs (ADR-0038). jsonb, weil er als Ganzes geschrieben
    /// und gelesen wird und nie der Suchschlüssel ist.
    /// </summary>
    public string ApplicantContact { get; set; } = "{}";
}

/// <summary>Eine Zeile von <c>application_drafts</c>.</summary>
/// <remarks>
/// Der Entwurf ist das FRUEHERE LEBEN derselben Sache — er wird zur JobApplication,
/// wenn ein Mensch zweimal geklickt hat. Deshalb liegt er in diesem Dienst und
/// nicht in einem eigenen: es ist derselbe fachliche Zusammenhang, nur eine
/// Stufe davor.
/// </remarks>
public sealed class DraftRow
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public Guid TenantId { get; set; }

    public Guid SubjectId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Der State, als Wort — wie bei der JobApplication, aus demselben Grund.</summary>
    public string Status { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    /// <summary>Die ART eines Fehlschlags, nie sein Inhalt.</summary>
    public string Error { get; set; } = string.Empty;

    public bool SharesResume { get; set; }

    /// <summary>Welche Unterlagen mitgehen, als jsonb-Liste von Kennungen.</summary>
    /// <remarks>
    /// Eine Liste und keine Verbundtabelle: sie wird immer als Ganzes
    /// geschrieben und als Ganzes gelesen, und eine Zeile je Beilage waere die
    /// Einladung, danach zu sortieren oder zu zaehlen.
    /// </remarks>
    public string Documents { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Start dieses Schreibens. Null, sobald es nicht mehr Entsteht.</summary>
    public DateTime? WritingStartedAt { get; set; }
}

/// <summary>Eine Zeile von <c>application_draft_comments</c>.</summary>
/// <remarks>
/// EIGENE Tabelle, anders als die Beilagen: Anmerkungen entstehen einzeln,
/// werden einzeln abgehakt und einzeln gelesen. Sie als jsonb zu halten hiesse,
/// bei jeder Anmerkung die ganze Liste neu zu schreiben.
/// </remarks>
public sealed class CommentRow
{
    public Guid Id { get; set; }

    public Guid DraftId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Die markierte Job, oder leer.</summary>
    public string Quote { get; set; } = string.Empty;

    public bool Resolved { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Die Tabellen, die dieser Dienst besitzt.</summary>
/// <remarks>
/// Es gibt keine gemeinsame Datenbank und deshalb auch keine gemeinsame Outbox
/// (ADR-0004/0025) — sie hängt in <em>diesen</em> Kontext, damit die Absicht mit
/// der Änderung committet, die sie ausgelöst hat.
/// <para>
/// Nachverfolgung ist für den ganzen Kontext aus statt je Abfrage. Je Abfrage
/// ist es Disziplin, und die nächste Abfrage, die jemand hinzufügt, vergisst
/// sie; der Speicher führt gelesene Werte ohnehin durch die Domäne zurück, die
/// sie normalisiert, sodass ein nachverfolgter Lesezugriff Zeilen als geändert
/// markieren und der nächste Commit Zeilen zurückschreiben würde, die niemand
/// angefasst hat.
/// </para>
/// </remarks>
public sealed class ApplicationsDbContext(DbContextOptions<ApplicationsDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Applications.</summary>
    public DbSet<ApplicationRow> Applications => Set<ApplicationRow>();

    /// <summary>Die Entwürfe.</summary>
    public DbSet<DraftRow> Entwuerfe => Set<DraftRow>();

    /// <summary>Die Anmerkungen an den Entwürfen.</summary>
    public DbSet<CommentRow> Anmerkungen => Set<CommentRow>();

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

            // Genau eine je (Person, Job) — in der Datenbank und nicht nur
            // im Handler, denn zwei gleichzeitige Absendungen kämen beide durch
            // eine Prüfung im Code. Zweimal auf dieselbe Job zu bewerben ist
            // kein Ausdruck von Interesse, sondern ein Versehen.
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
                .HasMaxLength(ApplicationDraft.HoechstlaengeBetreff).IsRequired();
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

            // KEIN eindeutiger Index auf (Job, Person): ein gesendeter
            // Entwurf bleibt als Evidence stehen, und danach darf ein neuer
            // entstehen. Die Eindeutigkeit sitzt an der BEWERBUNG, wo sie
            // hingehoert — zweimal zu bewerben ist das Versehen, zweimal zu
            // entwerfen nicht.
        });

        modelBuilder.Entity<CommentRow>(entity =>
        {
            entity.ToTable("application_draft_comments");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.DraftId).HasColumnName("draft_id").IsRequired();
            entity.Property(row => row.Body)
                .HasColumnName("body")
                .HasMaxLength(Anmerkung.HoechstlaengeText).IsRequired();
            entity.Property(row => row.Quote)
                .HasColumnName("quote")
                .HasMaxLength(Anmerkung.HoechstlaengeZitat).IsRequired();
            entity.Property(row => row.Resolved).HasColumnName("resolved").IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(row => row.DraftId);
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
