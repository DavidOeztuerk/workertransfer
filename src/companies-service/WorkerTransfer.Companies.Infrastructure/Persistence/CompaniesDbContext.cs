using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>company_profiles</c>.</summary>
public sealed class ProfileRow
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

    public string Line1 { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = "DE";

    public string Phone { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die eine Tabelle, die dieser Dienst besitzt.</summary>
/// <remarks>
/// None Outbox: dieser Dienst verschickt nichts. Und keine Löschtabelle — er
/// hält nichts über einen natürlichen Menschen, weshalb er auch nicht in
/// <c>ErasureRecipient.Fremde</c> steht (ADR-0027 §2). Ein Löschendpunkt hier
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
    /// <summary>Die EmployerProfiles.</summary>
    public DbSet<ProfileRow> Profile => Set<ProfileRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ProfileRow>(entity =>
        {
            entity.ToTable("company_profiles");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Slug)
                .HasColumnName("slug").HasMaxLength(Slug.Hoechstlaenge).IsRequired();
            entity.Property(row => row.DisplayName)
                .HasColumnName("display_name").HasColumnType("text").IsRequired();
            entity.Property(row => row.About)
                .HasColumnName("about").HasColumnType("text").IsRequired();
            entity.Property(row => row.Website)
                .HasColumnName("website").HasColumnType("text");

            // Listen als jsonb: sie werden nur als Ganzes gelesen und
            // geschrieben, und es gibt keine Abfrage über einzelne Einträge.
            entity.Property(row => row.Locations)
                .HasColumnName("locations").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Benefits)
                .HasColumnName("benefits").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Line1).HasColumnName("line1").HasColumnType("text").IsRequired();
            entity.Property(row => row.PostalCode).HasColumnName("postal_code").HasColumnType("text").IsRequired();
            entity.Property(row => row.City).HasColumnName("city").HasColumnType("text").IsRequired();
            entity.Property(row => row.Country).HasColumnName("country").HasColumnType("text").IsRequired();
            entity.Property(row => row.Phone).HasColumnName("phone").HasColumnType("text").IsRequired();

            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // Das Kürzel IST die Adresse der Karriere-Seite. Eindeutig in der
            // Datenbank und nicht nur im Handler: zwei gleichzeitige erste
            // Speicherungen kämen sonst beide durch die Search nach dem freien
            // Kürzel, und eine Adresse zeigte auf zwei Unternehmen.
            entity.HasIndex(row => row.Slug).IsUnique().HasDatabaseName("uq_company_slug");
        });

        base.OnModelCreating(modelBuilder);
    }
}
