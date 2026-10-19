using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>A row of <c>jobs</c>.</summary>
public sealed class JobRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>The postal code. Empty when none was given.</summary>
    public string PostalCode { get; set; } = string.Empty;

    public string RemoteMode { get; set; } = "none";

    public string EmploymentType { get; set; } = "full_time";

    /// <summary>The requirements as jsonb.</summary>
    public string Skills { get; set; } = "[]";

    public string Status { get; set; } = "draft";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    /// <summary>The lower end of the salary range, or <c>null</c>: not stated.</summary>
    public int? SalaryMin { get; set; }

    /// <summary>The upper end — set exactly when the lower one is.</summary>
    public int? SalaryMax { get; set; }

    /// <summary><c>year</c>, <c>month</c> or <c>hour</c>; <c>null</c> without a range.</summary>
    public string? SalaryPeriod { get; set; }

    /// <summary>The source it was fetched from, or <c>null</c>: written here (ADR-0052).</summary>
    public Guid? SourceId { get; set; }

    /// <summary>Its id at the source.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Its address at the source.</summary>
    public string? ExternalUrl { get; set; }
}

/// <summary>One row of <c>job_sources</c> — a company's ATS board or own feed (ADR-0052).</summary>
public sealed class JobSourceRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string Mode { get; set; } = "draft";

    public DateTime CreatedAt { get; set; }

    public DateTime? ProvenAt { get; set; }

    public DateTime? FetchedAt { get; set; }

    public string Outcome { get; set; } = string.Empty;
}

/// <summary>The one table of this service.</summary>
public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public DbSet<JobRow> Jobs => Set<JobRow>();

    public DbSet<JobSourceRow> Sources => Set<JobSourceRow>();

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
            row.Property(entry => entry.SalaryMin).HasColumnName("salary_min");
            row.Property(entry => entry.SalaryMax).HasColumnName("salary_max");
            row.Property(entry => entry.SalaryPeriod).HasColumnName("salary_period").HasMaxLength(8);

            // Both are paths that are searched along: a company's list and the
            // public page.
            row.Property(entry => entry.SourceId).HasColumnName("source_id");
            row.Property(entry => entry.ExternalId).HasColumnName("external_id").HasMaxLength(200);
            row.Property(entry => entry.ExternalUrl).HasColumnName("external_url").HasMaxLength(500);

            row.HasIndex(entry => entry.TenantId);
            row.HasIndex(entry => new { entry.Status, entry.PublishedAt });

            // One advert per id per source: a second row would be the same
            // advert twice, and the next fetch could not tell which to mirror.
            row.HasIndex(entry => new { entry.SourceId, entry.ExternalId })
                .IsUnique()
                .HasDatabaseName("uq_jobs_source_external")
                .HasFilter("source_id IS NOT NULL");
        });

        modelBuilder.Entity<JobSourceRow>(row =>
        {
            row.ToTable("job_sources");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id");
            row.Property(entry => entry.Kind).HasColumnName("kind").HasMaxLength(16).IsRequired();
            row.Property(entry => entry.Key).HasColumnName("key").HasMaxLength(300).IsRequired();
            row.Property(entry => entry.Domain).HasColumnName("domain").HasMaxLength(253).IsRequired();
            row.Property(entry => entry.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
            row.Property(entry => entry.Mode).HasColumnName("mode").HasMaxLength(8).IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at");
            row.Property(entry => entry.ProvenAt).HasColumnName("proven_at");
            row.Property(entry => entry.FetchedAt).HasColumnName("fetched_at");
            row.Property(entry => entry.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
            row.HasIndex(entry => entry.TenantId);
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Builds data source and context.</summary>
public static class JobsDbContextFactory
{
    /// <summary>The data source.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>The one place where tracking is switched off.</summary>
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>The same model for <c>dotnet ef</c>.</summary>
    public static DbContextOptionsBuilder AtDesignTime(
        DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>The context for <c>dotnet ef</c>.</summary>
public sealed class JobsDesignTimeFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    /// <inheritdoc />
    public JobsDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<JobsDbContext>)JobsDbContextFactory
            .AtDesignTime(
                new DbContextOptionsBuilder<JobsDbContext>(), "Host=design-time;Database=jobs")
            .Options);
}
