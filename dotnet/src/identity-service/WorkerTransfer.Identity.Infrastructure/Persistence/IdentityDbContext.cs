using Girder.Data.EntityFrameworkCore.Sessions;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Audit;

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

    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    public DbSet<MembershipRow> Memberships => Set<MembershipRow>();

    public DbSet<SessionCapacityRow> SessionCapacities => Set<SessionCapacityRow>();

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

        modelBuilder.Entity<AuditEventRow>(entity =>
        {
            entity.ToTable("audit_events", t => t.ExcludeFromMigrations());
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.ActorId).HasColumnName("actor_id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
            entity.Property(row => row.Action).HasColumnName("action");
            entity.Property(row => row.TargetId).HasColumnName("target_id");
            entity.Property(row => row.CorrelationId).HasColumnName("correlation_id");
            entity.Property(row => row.OccurredAt).HasColumnName("occurred_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        });

        modelBuilder.Entity<MembershipRow>(entity =>
        {
            entity.ToTable("user_tenant_memberships", t => t.ExcludeFromMigrations());
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.UserId).HasColumnName("user_id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
            entity.Property(row => row.Role).HasColumnName("role");
            entity.Property(row => row.GrantedAt).HasColumnName("granted_at");
        });

        modelBuilder.Entity<SessionCapacityRow>(entity =>
        {
            // Ours, so this one really is created by a migration here.
            entity.ToTable("session_capacities");
            entity.HasKey(row => row.SessionId);
            entity.Property(row => row.SessionId).HasColumnName("session_id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
        });

        modelBuilder.ConfigureGirderRefreshTokens();

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>One row of <c>audit_events</c>.</summary>
/// <remarks>
/// The trail is append-only, so there is no aggregate to rebuild and this row
/// is only ever written. <c>action</c> is the <c>audit_action</c> enum, carried
/// as the label it stores — see <c>AuditActionNames</c>.
/// </remarks>
public sealed class AuditEventRow
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public Guid? TenantId { get; set; }

    public AuditAction Action { get; set; }

    public Guid? TargetId { get; set; }

    public string? CorrelationId { get; set; }

    public DateTime OccurredAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>The <c>metadata</c> jsonb column, as written.</summary>
    public string Metadata { get; set; } = "{}";
}

/// <summary>One row of <c>user_tenant_memberships</c>.</summary>
public sealed class MembershipRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid TenantId { get; set; }

    public string Role { get; set; } = "member";

    public DateTime GrantedAt { get; set; }
}

/// <summary>One row of <c>session_capacities</c> — what a sign-in acts as.</summary>
/// <remarks>
/// One row per sign-in and not per refresh, because a session id is stable
/// across the whole rotation chain. A missing row means "acting as themselves",
/// so signing in as a person writes nothing here.
/// </remarks>
public sealed class SessionCapacityRow
{
    public Guid SessionId { get; set; }

    public Guid TenantId { get; set; }
}
