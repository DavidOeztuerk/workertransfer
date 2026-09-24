using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>A row of <c>mandates</c> — four values of one person.</summary>
/// <remarks>
/// <para><strong>What does NOT stand here is the content of this table.</strong>
/// No visibility, no release, no "for everyone" switch — none of that belongs to
/// a service that is not the ledger (ADR-0020). A test holds that no column of
/// this model carries <c>sichtbar</c>, <c>visible</c>, <c>public</c> or
/// <c>freigabe</c> in its name, and that is no quarrel about names: such a
/// column would be the second truth, and it would drift apart at the first
/// revocation.</para>
///
/// <para>And no availability: that lives in the market status at
/// transfer-service, together with the derived statement "approachable".
/// Mirroring it here would mean holding a statement that can cost somebody
/// their job in a place where nobody maintains it.</para>
/// </remarks>
public sealed class MandateRow
{
    /// <summary>The subject id. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    /// <summary>Month <c>YYYY-MM</c>, or <c>null</c>.</summary>
    public string? EntryMonth { get; set; }

    /// <summary>Euros per month, or <c>null</c>.</summary>
    public int? SalaryMin { get; set; }

    /// <summary>Euros per month, or <c>null</c>.</summary>
    public int? SalaryMax { get; set; }

    /// <summary>10–100, or <c>null</c>.</summary>
    public int? WorkloadPercent { get; set; }

    /// <summary>The excluded domains, as jsonb.</summary>
    /// <remarks>
    /// One column and not a second table: it is always read whole and replaced
    /// whole, there is no query about a single domain, and a table of its own
    /// would only bring a join and an order nobody needs.
    /// </remarks>
    public List<string> ExcludedDomains { get; set; } = [];

    /// <summary>When last written.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A row of <c>conversations</c>.</summary>
/// <remarks>
/// <strong>No stage column.</strong> Not <c>stage</c>, not <c>has_stage</c>, not
/// <c>released_at</c>: the stage is fetched from the ledger on every read
/// (ADR-0037 decision 2). A column here would be the stage as a second door
/// beside the ledger — and a revocation would then have to take effect in two
/// places.
/// <para>
/// And no message table: ADR-0037 decision 5 leaves open whether people type
/// here. As long as that is open, there is no table for it.
/// </para>
/// </remarks>
public sealed class ConversationRow
{
    /// <summary>Which conversation.</summary>
    public Guid Id { get; set; }

    /// <summary>With whom — this service's person column.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Which company.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Where it stands.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>What was written at the opening.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>When it began.</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>When something last changed.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>This service's two own tables — and the outbox.</summary>
/// <remarks>
/// Tracking is off, as the context's default and not per query. Per query it
/// would be discipline, and the next one added forgets it. A changing
/// <c>SaveAsync</c> therefore takes its <c>AsTracking()</c> explicitly.
/// </remarks>
public sealed class AdvisorDbContext(DbContextOptions<AdvisorDbContext> options) : DbContext(options)
{
    /// <summary>The mandates.</summary>
    public DbSet<MandateRow> Mandates => Set<MandateRow>();

    /// <summary>The conversations.</summary>
    public DbSet<ConversationRow> Conversations => Set<ConversationRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<MandateRow>(row =>
        {
            row.ToTable("mandates");
            // NO second column for the person. The key IS the person, and
            // `conversations` in the same file carries its own `subject_id`
            // beside it — whoever compares the two takes this for an oversight
            // and adds the column. Then two statements about the same human
            // being would stand side by side, and the erasure (ADR-0027) would
            // hit one and leave the other. The annotation says it instead: it
            // is what `ErasureRecipientTests` reads where no column name is to
            // be found.
            row.HasAnnotation(SubjectKeyedTable.Annotation, true);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.EntryMonth)
                .HasColumnName("entry_month").HasMaxLength(7);
            row.Property(entry => entry.SalaryMin).HasColumnName("salary_min");
            row.Property(entry => entry.SalaryMax).HasColumnName("salary_max");
            row.Property(entry => entry.WorkloadPercent).HasColumnName("workload_percent");
            row.Property(entry => entry.ExcludedDomains)
                .HasColumnName("excluded_domains").HasColumnType("jsonb").IsRequired();
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<ConversationRow>(row =>
        {
            row.ToTable("conversations");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.SubjectId).HasColumnName("subject_id").IsRequired();
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id").IsRequired();
            row.Property(entry => entry.State)
                .HasColumnName("state").HasMaxLength(16).IsRequired();
            row.Property(entry => entry.Note)
                .HasColumnName("note").HasMaxLength(2000).IsRequired();
            row.Property(entry => entry.OpenedAt).HasColumnName("opened_at").IsRequired();
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // The two queries there are: my conversations, and ours.
            row.HasIndex(entry => entry.SubjectId);
            row.HasIndex(entry => entry.TenantId);
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
