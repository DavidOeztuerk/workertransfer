using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>portfolios</c>.</summary>
/// <remarks>
/// Nicht das Aggregat. Die Einträge liegen als jsonb in einer Spalte und nicht
/// in einer zweiten Tabelle: sie werden immer vollständig gelesen und
/// vollständig ersetzt, es gibt keine Abfrage über einzelne Einträge, und eine
/// eigene Tabelle brächte nur eine Verknüpfung und eine Reihenfolge, die
/// niemand braucht.
/// </remarks>
public sealed class PortfolioRow
{
    /// <summary>Die <c>SubjectId</c>. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Die Einträge, unverarbeitet.</summary>
    public string Items { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die eine Tabelle dieses Dienstes.</summary>
/// <remarks>
/// Verfolgen ist aus, als Voreinstellung des Kontexts und nicht je Abfrage. Je
/// Abfrage wäre es Disziplin, und die nächste hinzugefügte vergisst es — und
/// die Abbildung führt gespeicherte Werte durch den Domänenkonstruktor, der
/// normalisiert. Ein verfolgtes Lesen schriebe beim nächsten Speichern Zeilen
/// um, die niemand anfassen wollte.
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
