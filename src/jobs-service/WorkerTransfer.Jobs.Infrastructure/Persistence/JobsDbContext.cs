using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>jobs</c>.</summary>
public sealed class JobRow
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
    public DbSet<JobRow> Jobs => Set<JobRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<JobRow>(row =>
        {
            row.ToTable("jobs");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id");
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id");
            row.Property(entry => entry.Title).HasColumnName("title").IsRequired();
            row.Property(entry => entry.Description)
                .HasColumnName("description").IsRequired();
            row.Property(entry => entry.Location).HasColumnName("location").IsRequired();
            row.Property(entry => entry.PostalCode)
                .HasColumnName("postal_code").IsRequired().HasDefaultValue(string.Empty);
            row.Property(entry => entry.RemoteMode).HasColumnName("remote_mode").IsRequired();
            row.Property(entry => entry.EmploymentType)
                .HasColumnName("employment_type").IsRequired();
            row.Property(entry => entry.Skills)
                .HasColumnName("skills").HasColumnType("jsonb").IsRequired();
            row.Property(entry => entry.Status).HasColumnName("status").IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at");
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at");
            row.Property(entry => entry.PublishedAt).HasColumnName("published_at");

            // Beides sind Wege, auf denen gesucht wird: die Liste eines
            // Unternehmens und die öffentliche Seite.
            row.HasIndex(entry => entry.TenantId);
            row.HasIndex(entry => new { entry.Status, entry.PublishedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Baut DataSource und Kontext.</summary>
public static class JobsDbContextFactory
{
    /// <summary>Die DataSource.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Die eine Job, an der Verfolgen abgeschaltet wird.</summary>
    public static DbContextOptionsBuilder Configure(
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
