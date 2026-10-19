using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Infrastructure.Persistence;

/// <summary>One row of <c>portfolios</c>.</summary>
/// <remarks>
/// Not the aggregate. The entries lie as jsonb in one column and not in a
/// second table: they are always read in full and replaced in full, there is
/// no query over individual entries, and a table of their own would bring
/// nothing but a join and an ordering nobody needs.
/// </remarks>
public sealed class PortfolioRow
{
    /// <summary>The <c>SubjectId</c>. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    /// <summary>The entries, unprocessed.</summary>
    public string Items { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>The one table of this service.</summary>
/// <remarks>
/// Tracking is off, as a default of the context and not per query. Per query it
/// would be discipline, and the next query somebody adds forgets it — and the
/// mapping leads stored values through the domain constructor, which
/// normalises. A tracked read would rewrite rows at the next save that nobody
/// meant to touch.
/// </remarks>
public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options)
    : DbContext(options)
{
    /// <summary>The audit trail. Survives the erasure it records.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    public DbSet<PortfolioRow> Portfolios => Set<PortfolioRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PortfolioRow>(row =>
        {
            row.ToTable("portfolios");
            row.HasAnnotation(SubjectKeyedTable.Annotation, true);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id");
            row.Property(entry => entry.Items)
                .HasColumnName("items").HasColumnType("jsonb").IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at");
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at");
        });

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
