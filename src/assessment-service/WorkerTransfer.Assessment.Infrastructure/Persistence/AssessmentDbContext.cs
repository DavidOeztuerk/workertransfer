using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Infrastructure.Persistence;

/// <summary>A row of <c>assessments</c> — a whole case.</summary>
/// <remarks>
/// <para><strong>No column holds a number about a human being.</strong> No
/// <c>score</c>, no <c>rating</c>, no stars, no grade field — and a test reads
/// the <em>EF model</em>, not the source, so that a column arising from a
/// convention stands out too (ADR-0042 §1).</para>
///
/// <para><c>hours</c> is a number and may be one: it is about the
/// <em>task</em>, not about the human being. That is exactly ADR-0022's
/// dividing line — requirement in, evidence out, never human in, number
/// out.</para>
///
/// <para><strong>And no state column.</strong> Where a case stands is computed
/// at every read from submission, evaluation and the clock. A column would
/// need somebody to flip it when a deadline passes — a nightly run touching
/// cases about human beings without anybody having asked.</para>
///
/// <para><strong>And exactly one evaluation field.</strong> No second one beside
/// it, no internal remark: where there were two, the truth would stand in the
/// second, and the person would read the other (ADR-0042 §2).</para>
/// </remarks>
public sealed class CaseRow
{
    /// <summary>Which case.</summary>
    public Guid Id { get; set; }

    /// <summary>Whom the task was set for — this service's person column.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Which company set it.</summary>
    public Guid TenantId { get; set; }

    /// <summary>The heading.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>What is to be done.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>The stated size in hours — about the task, never about the human being.</summary>
    public int Hours { get; set; }

    /// <summary>By when.</summary>
    public DateTime DueAt { get; set; }

    /// <summary>The text accompanying the solution, or <c>null</c>.</summary>
    public string? SubmissionText { get; set; }

    /// <summary>Where the solution points, or <c>null</c>. Never fetched.</summary>
    public string? SubmissionUrl { get; set; }

    /// <summary>When it was submitted, or <c>null</c>.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>The one feedback, or <c>null</c>.</summary>
    public string? EvaluationText { get; set; }

    /// <summary><c>accepted</c> or <c>rejected</c>, or <c>null</c>.</summary>
    public string? EvaluationOutcome { get; set; }

    /// <summary>When it was evaluated, or <c>null</c>.</summary>
    public DateTime? EvaluatedAt { get; set; }

    /// <summary>When the task was set.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When something last moved.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>This service's one table of its own — and the outbox.</summary>
/// <remarks>
/// Tracking is off, as the context's default and not per query. Per query it
/// would be discipline, and the next one added forgets it. A mutating
/// <c>SaveAsync</c> therefore takes its <c>AsTracking()</c> explicitly.
/// </remarks>
public sealed class AssessmentDbContext(DbContextOptions<AssessmentDbContext> options)
    : DbContext(options)
{
    /// <summary>The cases.</summary>
    public DbSet<CaseRow> Cases => Set<CaseRow>();

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

            // The two queries there are: my cases, and ours. A third — "the
            // cases of THIS person", asked by a company — does not exist, and so
            // there is no index for it either.
            row.HasIndex(entry => entry.SubjectId);
            row.HasIndex(entry => entry.TenantId);
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
