using Noelia.Data.EntityFrameworkCore.Sessions;
using WorkerTransfer.Outbox;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.ServiceDefaults;

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

    /// <summary>The <c>account_status</c> enum column.</summary>
    public AccountStatus Status { get; set; }

    /// <summary>The <c>roles</c> jsonb column, unparsed.</summary>
    public string Roles { get; set; } = "[]";

    /// <summary>The company meant at registration. <c>null</c> means a person.</summary>
    public string? PendingCompanyName { get; set; }

    /// <summary>The civil given name. Nullable — Art. 25, ADR-0038.</summary>
    public string? GivenName { get; set; }

    /// <summary>The civil family name.</summary>
    public string? FamilyName { get; set; }

    /// <summary>The <c>language</c> column: two letters, never null.</summary>
    /// <remarks>
    /// A plain string and not the enum: a mail written by a dispatcher days
    /// later reads this row, and a value the database refuses to widen would
    /// make adding a fourth language a migration of the column type rather than
    /// of the catalogue. The domain still narrows it on the way in.
    /// </remarks>
    public string Language { get; set; } = "de";

    /// <summary>The <c>berufsfeld</c> column: a label, or <c>null</c>.</summary>
    /// <remarks>
    /// Nullable with no default (ADR-0039): existing rows keep the neutral view.
    /// A string and not the enumeration, as with <see cref="Language"/> — a
    /// twelfth field is then no migration of the column type.
    /// </remarks>
    public string? OccupationalField { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int Version { get; set; }
}

/// <summary>What a person decided about themselves.</summary>
/// <remarks>
/// <strong>A subject-keyed table</strong> (see the annotation below): the key IS
/// the person, there is no `subject_id` column beside it. The erasure guard
/// recognises such tables by the annotation and not by the column name —
/// otherwise it would miss precisely the ones that hold the most.
/// </remarks>
public sealed class AccountSettingsRow
{
    /// <summary>Whose. And at the same time the key.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>The lapse in months, or <c>null</c> for "never by itself".</summary>
    public int? DeleteAfterMonths { get; set; }

    /// <summary>The AI provider as a label: none, openai_compatible, anthropic.</summary>
    public string AiProvider { get; set; } = "none";

    /// <summary>Its address.</summary>
    public string AiBaseUrl { get; set; } = string.Empty;

    /// <summary>Its model.</summary>
    public string AiModel { get; set; } = string.Empty;

    /// <summary>The key, encrypted. Never in the clear.</summary>
    public string AiKeyEncrypted { get; set; } = string.Empty;

    /// <summary>The last four characters, for recognising it again.</summary>
    public string AiKeyTail { get; set; } = string.Empty;

    /// <summary>Whether it is recorded THAT a request went out.</summary>
    public bool AiAuditLog { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// The application address — a template for a letterhead, never for the search, never for AI.
/// </summary>
/// <remarks>
/// A subject-keyed table: the key IS the person. Without the annotation the
/// erasure guard would not find the table (ADR-0027/0038).
/// </remarks>
public sealed class PostalAddressRow
{
    public Guid SubjectId { get; set; }

    public string Line1 { get; set; } = string.Empty;

    public string Line2 { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Country { get; set; } = "DE";

    public string Phone { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
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

    /// <summary>What a person decided about themselves.</summary>
    public DbSet<AccountSettingsRow> AccountSettings => Set<AccountSettingsRow>();

    /// <summary>The application address. A subject-keyed table.</summary>
    public DbSet<PostalAddressRow> Addresses => Set<PostalAddressRow>();

    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<InvitationRow> Invitations => Set<InvitationRow>();

    public DbSet<VerificationTokenRow> VerificationTokens => Set<VerificationTokenRow>();

    public DbSet<MembershipRow> Memberships => Set<MembershipRow>();

    public DbSet<SessionCapacityRow> SessionCapacities => Set<SessionCapacityRow>();

    public DbSet<NoeliaRefreshToken> RefreshTokens => Set<NoeliaRefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<UserRow>(entity =>
        {
            // Until the migration Alembic owned this table; the mapping carried
            // `ExcludeFromMigrations()` for that. With the Python service the
            // exception falls away: this service now creates its schema itself,
            // like the other ten.
            entity.ToTable("users");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.Email).HasColumnName("email").HasColumnType("citext");
            entity.Property(row => row.PasswordHash).HasColumnName("password_hash");
            entity.Property(row => row.DisplayName).HasColumnName("display_name");
            entity.Property(row => row.GivenName).HasColumnName("given_name");
            entity.Property(row => row.FamilyName).HasColumnName("family_name");
            entity.Property(row => row.Status).HasColumnName("status");
            entity.Property(row => row.Roles).HasColumnName("roles").HasColumnType("jsonb");
            entity.Property(row => row.PendingCompanyName).HasColumnName("pending_company_name");
            // The default belongs in the model, not in a migration. It used to
            // live only in the one that added the column, and a regenerated
            // schema therefore dropped it — a row written without a language
            // then fails the NOT NULL constraint instead of reading "de".
            entity.Property(row => row.Language).HasColumnName("language")
                .HasDefaultValue("de");
            entity.Property(row => row.OccupationalField).HasColumnName("berufsfeld");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.Version).HasColumnName("version").IsConcurrencyToken();
        });

        modelBuilder.Entity<AccountSettingsRow>(entity =>
        {
            entity.ToTable("account_settings");
            // `SubjectKeyedTable`: the key IS the person. Without this
            // annotation the erasure guard would not find the table — it looks
            // for column names AND for this annotation, precisely because such
            // tables have no `subject_id` beside the key.
            entity.HasAnnotation(SubjectKeyedTable.Annotation, true);
            entity.HasKey(row => row.SubjectId);
            entity.Property(row => row.SubjectId).HasColumnName("subject_id");
            entity.Property(row => row.DeleteAfterMonths).HasColumnName("delete_after_months");
            entity.Property(row => row.AiProvider).HasColumnName("ai_provider");
            entity.Property(row => row.AiBaseUrl).HasColumnName("ai_base_url");
            entity.Property(row => row.AiModel).HasColumnName("ai_model");
            entity.Property(row => row.AiKeyEncrypted).HasColumnName("ai_key_encrypted");
            entity.Property(row => row.AiKeyTail).HasColumnName("ai_key_tail");
            entity.Property(row => row.AiAuditLog).HasColumnName("ai_audit_log");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<PostalAddressRow>(entity =>
        {
            entity.ToTable("addresses");
            entity.HasAnnotation(SubjectKeyedTable.Annotation, true);
            entity.HasKey(row => row.SubjectId);
            entity.Property(row => row.SubjectId).HasColumnName("subject_id");
            entity.Property(row => row.Line1).HasColumnName("line1");
            entity.Property(row => row.Line2).HasColumnName("line2");
            entity.Property(row => row.PostalCode).HasColumnName("postal_code");
            entity.Property(row => row.City).HasColumnName("city");
            entity.Property(row => row.Country).HasColumnName("country");
            entity.Property(row => row.Phone).HasColumnName("phone");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<AuditEventRow>(entity =>
        {
            entity.ToTable("audit_events");
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
            entity.ToTable("user_tenant_memberships");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.UserId).HasColumnName("user_id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
            entity.Property(row => row.Role).HasColumnName("role");
            entity.Property(row => row.GrantedAt).HasColumnName("granted_at");

            // Same reason: a membership is written in the same transaction as
            // the company it points at.
            entity.HasOne<UserRow>().WithMany().HasForeignKey(row => row.UserId);
            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });

        modelBuilder.Entity<TenantRow>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.Name).HasColumnName("name");
            entity.Property(row => row.Domain).HasColumnName("domain").HasColumnType("citext");
            entity.Property(row => row.Status).HasColumnName("status");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<VerificationTokenRow>(entity =>
        {
            entity.ToTable("email_verification_tokens");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.UserId).HasColumnName("user_id");
            entity.Property(row => row.TokenHash).HasColumnName("token_hash");
            entity.Property(row => row.Purpose).HasColumnName("purpose");
            entity.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            entity.Property(row => row.ConsumedAt).HasColumnName("consumed_at");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");

            // Not for the schema — Alembic owns that — but for the order. EF
            // does not know a foreign key it was not told about, so without
            // this it may insert the token before the account it belongs to,
            // which is exactly what registering does in one transaction.
            entity.HasOne<UserRow>().WithMany().HasForeignKey(row => row.UserId);
        });

        modelBuilder.Entity<InvitationRow>(entity =>
        {
            entity.ToTable("company_invitations");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
            entity.Property(row => row.Email).HasColumnName("email").HasColumnType("citext");
            entity.Property(row => row.Role).HasColumnName("role");
            entity.Property(row => row.InvitedBy).HasColumnName("invited_by");
            entity.Property(row => row.Status).HasColumnName("status");
            entity.Property(row => row.TokenHash).HasColumnName("token_hash");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.ExpiresAt).HasColumnName("expires_at");
            entity.Property(row => row.AcceptedAt).HasColumnName("accepted_at");

            entity.HasOne<TenantRow>().WithMany().HasForeignKey(row => row.TenantId);
        });

        modelBuilder.Entity<SessionCapacityRow>(entity =>
        {
            // Ours, so this one really is created by a migration here.
            entity.ToTable("session_capacities");
            entity.HasKey(row => row.SessionId);
            entity.Property(row => row.SessionId).HasColumnName("session_id");
            entity.Property(row => row.TenantId).HasColumnName("tenant_id");
        });

        // Ours, and in OUR database: the intent commits together with the
        // change that caused it. At a central service it would be a
        // distributed transaction — exactly what the pattern avoids.
        modelBuilder.ConfigureOutbox();

        modelBuilder.ConfigureNoeliaRefreshTokens();

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

/// <summary>One row of <c>tenants</c>.</summary>
public sealed class TenantRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>citext, so the uniqueness of a domain is case-insensitive.</summary>
    public string Domain { get; set; } = string.Empty;

    public string Status { get; set; } = "active";

    public DateTime CreatedAt { get; set; }
}

/// <summary>One row of <c>email_verification_tokens</c>.</summary>
/// <remarks>Only the hash. A leaked row must not be an account takeover.</remarks>
public sealed class VerificationTokenRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>One row of <c>company_invitations</c>.</summary>
/// <remarks>
/// <c>invited_by</c> is nullable and its foreign key is <c>SET NULL</c>, not
/// <c>CASCADE</c> — and the difference is not a nicety (ADR-0027 §2). With
/// cascade, a company's open invitations vanished the moment a recruiter
/// deleted their <em>private</em> account. The invitation belongs to the
/// company; what falls away is the name on it.
/// </remarks>
public sealed class InvitationRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = "member";

    public Guid? InvitedBy { get; set; }

    public string Status { get; set; } = "pending";

    /// <summary>Only the hash. The plaintext goes out by mail and stands nowhere here.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? AcceptedAt { get; set; }
}
