using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>applications</c>.</summary>
/// <remarks>
/// Nicht das Aggregat. <see cref="EfBewerbungsspeicher"/> baut daraus eine
/// <c>Bewerbung</c>, damit nichts, was am Aggregat geschieht, von allein in der
/// Tabelle landet.
/// </remarks>
public sealed class BewerbungsZeile
{
    /// <summary>Welche Bewerbung.</summary>
    public Guid Id { get; set; }

    /// <summary>Auf welche Stelle.</summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Welches Unternehmen — aus der Stelle kopiert.
    /// </summary>
    /// <remarks>
    /// Ein Fremdschlüssel geht nicht: die Stellen liegen in einer anderen
    /// Datenbank (ADR-0004). Eine Kopie ist nur gefährlich, wenn das Original
    /// sich ändern kann, und eine Stelle wechselt nicht das Unternehmen.
    /// </remarks>
    public Guid TenantId { get; set; }

    /// <summary>Wer sich beworben hat.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Was die Person geschrieben hat.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Ob der Lebenslauf mitging.</summary>
    public bool SharesResume { get; set; }

    /// <summary>Ob das Portfolio mitging.</summary>
    public bool SharesPortfolio { get; set; }

    /// <summary>Welche Unterlagen beilagen, als jsonb-Liste von Kennungen.</summary>
    public string Documents { get; set; } = "[]";

    /// <summary>Der Stand, als Wort.</summary>
    /// <remarks>
    /// Als <c>varchar</c> und nicht als Postgres-Enum: der Stand steht so auch
    /// im Vertrag und in der Oberfläche, und ein Typ, der bei jeder Erweiterung
    /// eine eigene Wanderung braucht, kauft hier nichts.
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
/// Der Entwurf ist das FRUEHERE LEBEN derselben Sache — er wird zur Bewerbung,
/// wenn ein Mensch zweimal geklickt hat. Deshalb liegt er in diesem Dienst und
/// nicht in einem eigenen: es ist derselbe fachliche Zusammenhang, nur eine
/// Stufe davor.
/// </remarks>
public sealed class EntwurfsZeile
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public Guid TenantId { get; set; }

    public Guid SubjectId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Der Stand, als Wort — wie bei der Bewerbung, aus demselben Grund.</summary>
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
public sealed class AnmerkungsZeile
{
    public Guid Id { get; set; }

    public Guid DraftId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Die markierte Stelle, oder leer.</summary>
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
    /// <summary>Die Bewerbungen.</summary>
    public DbSet<BewerbungsZeile> Bewerbungen => Set<BewerbungsZeile>();

    /// <summary>Die Entwürfe.</summary>
    public DbSet<EntwurfsZeile> Entwuerfe => Set<EntwurfsZeile>();

    /// <summary>Die Anmerkungen an den Entwürfen.</summary>
    public DbSet<AnmerkungsZeile> Anmerkungen => Set<AnmerkungsZeile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<BewerbungsZeile>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.JobId).HasColumnName("job_id").IsRequired();
            entity.Property(zeile => zeile.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(zeile => zeile.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(zeile => zeile.Message)
                .HasColumnName("message").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.SharesResume)
                .HasColumnName("shares_resume").IsRequired();
            entity.Property(zeile => zeile.SharesPortfolio)
                .HasColumnName("shares_portfolio").IsRequired();
            entity.Property(zeile => zeile.Documents)
                .HasColumnName("documents").HasColumnType("jsonb")
                .HasDefaultValue("[]").IsRequired();
            entity.Property(zeile => zeile.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(zeile => zeile.AnsweredAt).HasColumnName("answered_at");
            entity.Property(zeile => zeile.ApplicantContact)
                .HasColumnName("applicant_contact")
                .HasColumnType("jsonb")
                .HasDefaultValue("{}")
                .IsRequired();

            entity.HasIndex(zeile => zeile.JobId);
            entity.HasIndex(zeile => zeile.TenantId);
            entity.HasIndex(zeile => zeile.SubjectId);

            // Genau eine je (Person, Stelle) — in der Datenbank und nicht nur
            // im Handler, denn zwei gleichzeitige Absendungen kämen beide durch
            // eine Prüfung im Code. Zweimal auf dieselbe Stelle zu bewerben ist
            // kein Ausdruck von Interesse, sondern ein Versehen.
            entity.HasIndex(zeile => new { zeile.JobId, zeile.SubjectId })
                .IsUnique()
                .HasDatabaseName("uq_application_job_subject");
        });

        modelBuilder.Entity<EntwurfsZeile>(entity =>
        {
            entity.ToTable("application_drafts");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.JobId).HasColumnName("job_id").IsRequired();
            entity.Property(zeile => zeile.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(zeile => zeile.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(zeile => zeile.Subject)
                .HasColumnName("subject")
                .HasMaxLength(Bewerbungsentwurf.HoechstlaengeBetreff).IsRequired();
            entity.Property(zeile => zeile.Body)
                .HasColumnName("body").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(zeile => zeile.Version).HasColumnName("version").IsRequired();
            entity.Property(zeile => zeile.Error)
                .HasColumnName("error").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.SharesResume)
                .HasColumnName("shares_resume").IsRequired();
            entity.Property(zeile => zeile.Documents)
                .HasColumnName("documents").HasColumnType("jsonb").IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(zeile => zeile.WritingStartedAt).HasColumnName("writing_started_at");

            entity.HasIndex(zeile => zeile.SubjectId);

            // KEIN eindeutiger Index auf (Stelle, Person): ein gesendeter
            // Entwurf bleibt als Beleg stehen, und danach darf ein neuer
            // entstehen. Die Eindeutigkeit sitzt an der BEWERBUNG, wo sie
            // hingehoert — zweimal zu bewerben ist das Versehen, zweimal zu
            // entwerfen nicht.
        });

        modelBuilder.Entity<AnmerkungsZeile>(entity =>
        {
            entity.ToTable("application_draft_comments");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.DraftId).HasColumnName("draft_id").IsRequired();
            entity.Property(zeile => zeile.Body)
                .HasColumnName("body")
                .HasMaxLength(Anmerkung.HoechstlaengeText).IsRequired();
            entity.Property(zeile => zeile.Quote)
                .HasColumnName("quote")
                .HasMaxLength(Anmerkung.HoechstlaengeZitat).IsRequired();
            entity.Property(zeile => zeile.Resolved).HasColumnName("resolved").IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();

            entity.HasIndex(zeile => zeile.DraftId);
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
