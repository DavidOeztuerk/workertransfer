using Girder.Data.EntityFrameworkCore.Sessions;
using Microsoft.EntityFrameworkCore;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>One row of <c>users</c>, as it stands in the database.</summary>
/// <remarks>
/// Not the aggregate. <see cref="EfUserRepository"/> builds a
/// <see cref="User"/> from it, so nothing done to the aggregate can reach the
/// table — the property twenty thousand lines of Python were written against.
/// </remarks>
public sealed class UserRow
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The <c>account_status</c> enum column, as its stored name.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The <c>roles</c> jsonb column, unparsed.</summary>
    public string Roles { get; set; } = "[]";

    public int Version { get; set; }
}

/// <summary>The tables this service reads and writes.</summary>
/// <remarks>
/// Tracking is off for the whole context rather than per query. Per query it is
/// discipline, and the next query somebody adds forgets it — while
/// <see cref="EfUserRepository"/> runs stored values back through the domain,
/// which normalises them, so a tracked read would mark rows modified and the
/// next save would rewrite rows nobody touched.
/// </remarks>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<UserRow> Users => Set<UserRow>();

    public DbSet<GirderRefreshToken> RefreshTokens => Set<GirderRefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<UserRow>(entity =>
        {
            // Alembic owns this table until the Python service is gone; a
            // migration generated here would try to create it.
            entity.ToTable("users", t => t.ExcludeFromMigrations());
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.Email).HasColumnName("email").HasColumnType("citext");
            entity.Property(row => row.PasswordHash).HasColumnName("password_hash");
            entity.Property(row => row.DisplayName).HasColumnName("display_name");
            entity.Property(row => row.Status).HasColumnName("status");
            entity.Property(row => row.Roles).HasColumnName("roles").HasColumnType("jsonb");
            entity.Property(row => row.Version).HasColumnName("version").IsConcurrencyToken();
        });

        modelBuilder.ConfigureGirderRefreshTokens();

        base.OnModelCreating(modelBuilder);
    }
}
