using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>company_profiles</c>.</summary>
public sealed class ProfilZeile
{
    /// <summary>Die Mandanten-Kennung. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Die Adresse der Karriere-Seite. Eindeutig.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Die Marke.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Was das Unternehmen über sich schreibt.</summary>
    public string About { get; set; } = string.Empty;

    /// <summary><c>null</c>, wenn keine angegeben ist.</summary>
    public string? Website { get; set; }

    /// <summary>Die <c>locations</c>-Spalte, als jsonb.</summary>
    public string Locations { get; set; } = "[]";

    /// <summary>Die <c>benefits</c>-Spalte, als jsonb.</summary>
    public string Benefits { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die eine Tabelle, die dieser Dienst besitzt.</summary>
/// <remarks>
/// Keine Outbox: dieser Dienst verschickt nichts. Und keine Löschtabelle — er
/// hält nichts über einen natürlichen Menschen, weshalb er auch nicht in
/// <c>Loeschempfaenger.Fremde</c> steht (ADR-0027 §2). Ein Löschendpunkt hier
/// wäre einer, der „erledigt" sagt, ohne je etwas getan zu haben.
/// <para>
/// Nachverfolgung ist für den ganzen Kontext aus. Der Speicher führt gelesene
/// Werte durch die Domäne zurück, die sie normalisiert; ein nachverfolgter
/// Lesezugriff würde Zeilen als geändert markieren und der nächste Commit
/// Zeilen zurückschreiben, die niemand angefasst hat.
/// </para>
/// </remarks>
public sealed class CompaniesDbContext(DbContextOptions<CompaniesDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Arbeitgeberprofile.</summary>
    public DbSet<ProfilZeile> Profile => Set<ProfilZeile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ProfilZeile>(entity =>
        {
            entity.ToTable("company_profiles");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.Slug)
                .HasColumnName("slug").HasMaxLength(Kuerzel.Hoechstlaenge).IsRequired();
            entity.Property(zeile => zeile.DisplayName)
                .HasColumnName("display_name").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.About)
                .HasColumnName("about").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.Website)
                .HasColumnName("website").HasColumnType("text");

            // Listen als jsonb: sie werden nur als Ganzes gelesen und
            // geschrieben, und es gibt keine Abfrage über einzelne Einträge.
            entity.Property(zeile => zeile.Locations)
                .HasColumnName("locations").HasColumnType("jsonb").IsRequired();
            entity.Property(zeile => zeile.Benefits)
                .HasColumnName("benefits").HasColumnType("jsonb").IsRequired();

            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // Das Kürzel IST die Adresse der Karriere-Seite. Eindeutig in der
            // Datenbank und nicht nur im Handler: zwei gleichzeitige erste
            // Speicherungen kämen sonst beide durch die Suche nach dem freien
            // Kürzel, und eine Adresse zeigte auf zwei Unternehmen.
            entity.HasIndex(zeile => zeile.Slug).IsUnique().HasDatabaseName("uq_company_slug");
        });

        base.OnModelCreating(modelBuilder);
    }
}
