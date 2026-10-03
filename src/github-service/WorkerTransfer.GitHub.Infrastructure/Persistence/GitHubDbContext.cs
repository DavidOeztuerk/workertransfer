using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>One row of <c>github_connections</c>.</summary>
/// <remarks>
/// The snapshot lies beside it as jsonb, not in a table of its own: it is
/// always written as a whole and read as a whole, never queried individually. A
/// second table would be a join for a value that only makes sense together —
/// and one row per repository would be the invitation to sort by it.
/// </remarks>
public sealed class ConnectionRow
{
    /// <summary>The subject id. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    /// <summary><c>null</c>: no account named yet.</summary>
    public string? Login { get; set; }

    public string Challenge { get; set; } = string.Empty;

    public DateTime? VerifiedAt { get; set; }

    public DateTime? FetchedAt { get; set; }

    /// <summary>The snapshot, as jsonb.</summary>
    public string Repositories { get; set; } = "[]";

    /// <summary>Could the languages be fetched for every repository?</summary>
    public bool LanguagesComplete { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>The one table this service owns.</summary>
/// <remarks>
/// No outbox: this service sends nothing. And no column that summarises a human
/// being — no score, no rank, no derived skill (ADR-0022).
/// </remarks>
public sealed class GitHubDbContext(DbContextOptions<GitHubDbContext> options)
    : DbContext(options)
{
    /// <summary>The audit trail. Survives the erasure it records.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    /// <summary>The connections.</summary>
    public DbSet<ConnectionRow> Connections => Set<ConnectionRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ConnectionRow>(entity =>
        {
            entity.ToTable("github_connections");
            entity.HasAnnotation(SubjectKeyedTable.Annotation, true);
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            // WITHOUT `IsRequired`: a connection that comes into being over
            // GitHub's sign-in does not know its account yet — GitHub reports
            // the name. An empty string instead of `null` would be a name that
            // does not exist, and it would be counted in every query.
            entity.Property(row => row.Login)
                .HasColumnName("login")
                .HasMaxLength(Connection.MaxLoginLength);
            entity.Property(row => row.Challenge)
                .HasColumnName("challenge").HasMaxLength(64).IsRequired();
            entity.Property(row => row.VerifiedAt).HasColumnName("verified_at");
            entity.Property(row => row.FetchedAt).HasColumnName("fetched_at");
            entity.Property(row => row.Repositories)
                .HasColumnName("repositories").HasColumnType("jsonb").IsRequired();
            // Default `true`: existing rows stem from a time in which no
            // languages were fetched at all without a token. Setting `false`
            // would be more honest about the past and more tiresome for the
            // present — the next fetch writes the true value anyway, and until
            // then the row asserts nothing it could not back up: it simply
            // shows what it has.
            entity.Property(row => row.LanguagesComplete)
                .HasColumnName("languages_complete").HasDefaultValue(true).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();
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
