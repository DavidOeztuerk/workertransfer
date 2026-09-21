using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>One row of <c>company_profiles</c>.</summary>
public sealed class ProfileRow
{
    /// <summary>The tenant id. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    /// <summary>The address of the careers page. Unique.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>The brand.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>What the company writes about itself.</summary>
    public string About { get; set; } = string.Empty;

    /// <summary><c>null</c> when none is given.</summary>
    public string? Website { get; set; }

    /// <summary>The <c>locations</c> column, as jsonb.</summary>
    public string Locations { get; set; } = "[]";

    /// <summary>The <c>benefits</c> column, as jsonb.</summary>
    public string Benefits { get; set; } = "[]";

    public string Line1 { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = "DE";

    public string Phone { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>The one table this service owns.</summary>
/// <remarks>
/// No outbox: this service sends nothing. And no erasure table — it holds
/// nothing about a natural person, which is why it does not stand in
/// <c>ErasureRecipient.Fremde</c> either (ADR-0027 §2). An erasure endpoint
/// here would be one that says "done" without ever having done anything.
/// <para>
/// Tracking is off for the whole context. The store leads read values back
/// through the domain, which normalises them; a tracked read would mark rows
/// as changed and the next commit would write back rows nobody touched.
/// </para>
/// </remarks>
public sealed class CompaniesDbContext(DbContextOptions<CompaniesDbContext> options)
    : DbContext(options)
{
    /// <summary>The employer profiles.</summary>
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
                .HasColumnName("slug").HasMaxLength(Slug.MaxLength).IsRequired();
            entity.Property(row => row.DisplayName)
                .HasColumnName("display_name").HasColumnType("text").IsRequired();
            entity.Property(row => row.About)
                .HasColumnName("about").HasColumnType("text").IsRequired();
            entity.Property(row => row.Website)
                .HasColumnName("website").HasColumnType("text");

            // Lists as jsonb: they are read and written only as a whole, and
            // there is no query over individual entries.
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

            // The slug IS the address of the careers page. Unique in the
            // database and not only in the handler: two concurrent first saves
            // would otherwise both get through the search for the free slug,
            // and one address would point at two companies.
            entity.HasIndex(row => row.Slug).IsUnique().HasDatabaseName("uq_company_slug");
        });

        base.OnModelCreating(modelBuilder);
    }
}
