using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>assessments</c> — ein ganzer Case.</summary>
/// <remarks>
/// <para><strong>None Spalte hält eine Zahl über einen Menschen.</strong> Kein
/// <c>score</c>, kein <c>rating</c>, keine Sterne, kein Notenfeld — und ein Test
/// liest das <em>EF-Modell</em>, nicht den Quelltext, damit auch eine Spalte
/// auffällt, die aus einer Konvention entsteht (ADR-0042 §1).</para>
///
/// <para><c>hours</c> ist eine Zahl und darf es sein: sie handelt von der
/// <em>Aufgabe</em>, nicht vom Menschen. Genau das ist die Trennlinie von
/// ADR-0022 — Anforderung rein, Belege raus, niemals Mensch rein, Zahl raus.</para>
///
/// <para><strong>Und keine Standspalte.</strong> Wo ein Case steht, wird bei
/// jedem Lesen aus Submission, Evaluation und der Uhr gerechnet. Eine Spalte
/// müsste jemand umschalten, wenn eine Frist abläuft — ein Nachtlauf, der
/// Vorgänge über Menschen anfasst, ohne dass jemand gefragt hat.</para>
///
/// <para><strong>Und genau ein Bewertungsfeld.</strong> Kein zweites daneben,
/// keine interne Notiz: wo es zwei gäbe, stünde im zweiten die Wahrheit, und
/// die Person läse das andere (ADR-0042 §2).</para>
/// </remarks>
public sealed class CaseRow
{
    /// <summary>Welcher Case.</summary>
    public Guid Id { get; set; }

    /// <summary>Wem die Aufgabe gestellt wurde — die Personenspalte dieses Dienstes.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Welches Unternehmen sie gestellt hat.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Die Überschrift.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Was zu tun ist.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>Der genannte Umfang in Stunden — über die Aufgabe, nie über den Menschen.</summary>
    public int Hours { get; set; }

    /// <summary>Bis wann.</summary>
    public DateTime DueAt { get; set; }

    /// <summary>Der Begleittext der Lösung, oder <c>null</c>.</summary>
    public string? SubmissionText { get; set; }

    /// <summary>Wohin die Lösung zeigt, oder <c>null</c>. Wird nie abgerufen.</summary>
    public string? SubmissionUrl { get; set; }

    /// <summary>Wann eingereicht wurde, oder <c>null</c>.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Die eine Rückmeldung, oder <c>null</c>.</summary>
    public string? EvaluationText { get; set; }

    /// <summary><c>accepted</c> oder <c>rejected</c>, oder <c>null</c>.</summary>
    public string? EvaluationOutcome { get; set; }

    /// <summary>Wann bewertet wurde, oder <c>null</c>.</summary>
    public DateTime? EvaluatedAt { get; set; }

    /// <summary>Wann die Aufgabe gestellt wurde.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Wann sich zuletzt etwas bewegt hat.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die eine eigene Tabelle dieses Dienstes — und der Postausgang.</summary>
/// <remarks>
/// Verfolgen ist aus, als Voreinstellung des Kontexts und nicht je Abfrage. Je
/// Abfrage wäre es Disziplin, und die nächste hinzugefügte vergisst es. Ein
/// änderndes <c>SaveAsync</c> nimmt sich sein <c>AsTracking()</c> deshalb
/// ausdrücklich.
/// </remarks>
public sealed class AssessmentDbContext(DbContextOptions<AssessmentDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Vorgänge.</summary>
    public DbSet<CaseRow> Vorgaenge => Set<CaseRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<CaseRow>(row =>
        {
            row.ToTable("assessments");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.SubjectId).HasColumnName("subject_id").IsRequired();
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id").IsRequired();
            row.Property(entry => entry.Title)
                .HasColumnName("title").HasMaxLength(200).IsRequired();
            row.Property(entry => entry.Task)
                .HasColumnName("task").HasMaxLength(8000).IsRequired();
            row.Property(entry => entry.Hours).HasColumnName("hours").IsRequired();
            row.Property(entry => entry.DueAt).HasColumnName("due_at").IsRequired();
            row.Property(entry => entry.SubmissionText)
                .HasColumnName("submission_text").HasMaxLength(8000);
            row.Property(entry => entry.SubmissionUrl)
                .HasColumnName("submission_url").HasMaxLength(2000);
            row.Property(entry => entry.SubmittedAt).HasColumnName("submitted_at");
            row.Property(entry => entry.EvaluationText)
                .HasColumnName("evaluation_text").HasMaxLength(8000);
            row.Property(entry => entry.EvaluationOutcome)
                .HasColumnName("evaluation_outcome").HasMaxLength(16);
            row.Property(entry => entry.EvaluatedAt).HasColumnName("evaluated_at");
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // Die zwei Abfragen, die es gibt: meine Vorgaenge, und unsere. Eine
            // dritte — „die Vorgaenge DIESER Person", von einer Firma gefragt —
            // gibt es nicht, und deshalb auch keinen Index dafuer.
            row.HasIndex(entry => entry.SubjectId);
            row.HasIndex(entry => entry.TenantId);
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
