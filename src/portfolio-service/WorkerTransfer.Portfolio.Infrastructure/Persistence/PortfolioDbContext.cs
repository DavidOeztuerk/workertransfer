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
    public DbSet<PortfolioRow> Portfolios => Set<PortfolioRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PortfolioRow>(row =>
        {
            row.ToTable("portfolios");
            row.HasAnnotation(SubjectKeyedTable.Anmerkung, true);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id");
            row.Property(entry => entry.Items)
                .HasColumnName("items").HasColumnType("jsonb").IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at");
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at");
        });

        base.OnModelCreating(modelBuilder);
    }
}
