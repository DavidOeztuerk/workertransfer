using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>jobs</c>.</summary>
public sealed class StellenZeile
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>Die Postleitzahl. Leer, wenn keine angegeben wurde.</summary>
    public string PostalCode { get; set; } = string.Empty;

    public string RemoteMode { get; set; } = "none";

    public string EmploymentType { get; set; } = "full_time";

    /// <summary>Die Anforderungen als jsonb.</summary>
    public string Skills { get; set; } = "[]";

    public string Status { get; set; } = "draft";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }
}

/// <summary>Die eine Tabelle dieses Dienstes.</summary>
public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public DbSet<StellenZeile> Stellen => Set<StellenZeile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<StellenZeile>(zeile =>
        {
            zeile.ToTable("jobs");
            zeile.HasKey(eintrag => eintrag.Id);
            zeile.Property(eintrag => eintrag.Id).HasColumnName("id");
            zeile.Property(eintrag => eintrag.TenantId).HasColumnName("tenant_id");
            zeile.Property(eintrag => eintrag.Title).HasColumnName("title").IsRequired();
            zeile.Property(eintrag => eintrag.Description)
                .HasColumnName("description").IsRequired();
            zeile.Property(eintrag => eintrag.Location).HasColumnName("location").IsRequired();
            zeile.Property(eintrag => eintrag.PostalCode)
                .HasColumnName("postal_code").IsRequired().HasDefaultValue(string.Empty);
            zeile.Property(eintrag => eintrag.RemoteMode).HasColumnName("remote_mode").IsRequired();
            zeile.Property(eintrag => eintrag.EmploymentType)
                .HasColumnName("employment_type").IsRequired();
            zeile.Property(eintrag => eintrag.Skills)
                .HasColumnName("skills").HasColumnType("jsonb").IsRequired();
            zeile.Property(eintrag => eintrag.Status).HasColumnName("status").IsRequired();
            zeile.Property(eintrag => eintrag.CreatedAt).HasColumnName("created_at");
            zeile.Property(eintrag => eintrag.UpdatedAt).HasColumnName("updated_at");
            zeile.Property(eintrag => eintrag.PublishedAt).HasColumnName("published_at");

            // Beides sind Wege, auf denen gesucht wird: die Liste eines
            // Unternehmens und die öffentliche Seite.
            zeile.HasIndex(eintrag => eintrag.TenantId);
            zeile.HasIndex(eintrag => new { eintrag.Status, eintrag.PublishedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Baut Datenquelle und Kontext.</summary>
public static class JobsDbContextFactory
{
    /// <summary>Die Datenquelle.</summary>
    public static NpgsqlDataSource Datenquelle(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Die eine Stelle, an der Verfolgen abgeschaltet wird.</summary>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>Dasselbe Modell für <c>dotnet ef</c>.</summary>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>Der Kontext für <c>dotnet ef</c>.</summary>
public sealed class JobsDesignTimeFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    /// <inheritdoc />
    public JobsDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<JobsDbContext>)JobsDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<JobsDbContext>(), "Host=entwurfszeit;Database=jobs")
            .Options);
}
