using Girder.Data.EntityFrameworkCore.Sessions;
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

    /// <summary>Bürgerlicher Vorname. Nullable — Art. 25, ADR-0038.</summary>
    public string? GivenName { get; set; }

    /// <summary>Bürgerlicher Nachname.</summary>
    public string? FamilyName { get; set; }

    /// <summary>The <c>language</c> column: two letters, never null.</summary>
    /// <remarks>
    /// A plain string and not the enum: a mail written by a dispatcher days
    /// later reads this row, and a value the database refuses to widen would
    /// make adding a fourth language a migration of the column type rather than
    /// of the catalogue. The domain still narrows it on the way in.
    /// </remarks>
    public string Language { get; set; } = "de";

    /// <summary>Die <c>berufsfeld</c>-Spalte: ein Etikett, oder <c>null</c>.</summary>
    /// <remarks>
    /// Nullbar, und das ist die Zusage aus ADR-0039: wer nichts wählt, bekommt
    /// die heutige Ansicht. Jede bestehende Zeile trägt nach der Wanderung
    /// <c>null</c> und sieht, was sie vorher sah — es gibt kein
    /// Datenwanderungs-Skript, weil jede Vermutung eine abgeleitete Eigenschaft
    /// über einen Menschen wäre.
    /// <para>
    /// Eine Zeichenkette und nicht die Aufzählung, aus demselben Grund wie bei
    /// <see cref="Language"/>: ein zwölftes Feld ist dann eine Ergänzung der
    /// Liste und keine Wanderung des Spaltentyps. Die Domäne verengt es auf dem
    /// Weg herein.
    /// </para>
    /// </remarks>
    public string? Berufsfeld { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int Version { get; set; }
}

/// <summary>Was eine Person über sich entschieden hat.</summary>
/// <remarks>
/// <strong>Eine Personenzeile</strong> (siehe die Anmerkung unten): der
/// Schlüssel IST die Person, es gibt keine `subject_id`-Spalte daneben. Der
/// Löschwächter erkennt solche Tabellen an der Anmerkung und nicht am
/// Spaltennamen — sonst übersähe er ausgerechnet die, die am meisten halten.
/// </remarks>
public sealed class KontoeinstellungenRow
{
    /// <summary>Wessen. Zugleich der Schlüssel.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Verfall in Monaten, oder <c>null</c> für „nie von selbst".</summary>
    public int? DeleteAfterMonths { get; set; }

    /// <summary>Der KI-Anbieter als Etikett: none, openai_compatible, anthropic.</summary>
    public string AiProvider { get; set; } = "none";

    /// <summary>Seine Adresse.</summary>
    public string AiBaseUrl { get; set; } = string.Empty;

    /// <summary>Sein Modell.</summary>
    public string AiModel { get; set; } = string.Empty;

    /// <summary>Der Schlüssel, verschlüsselt. Nie im Klartext.</summary>
    public string AiKeyEncrypted { get; set; } = string.Empty;

    /// <summary>Die letzten vier Zeichen, zum Wiedererkennen.</summary>
    public string AiKeyTail { get; set; } = string.Empty;

    /// <summary>Ob festgehalten wird, DASS eine Anfrage hinausging.</summary>
    public bool AiAuditLog { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Die Bewerbungsanschrift — Vorlage für Briefkopf, nie für Suche, nie für KI.
/// </summary>
/// <remarks>
/// Personenzeile: der Schlüssel IST die Person. Ohne die Anmerkung fände der
/// Löschwächter die Tabelle nicht (ADR-0027/0038).
/// </remarks>
public sealed class AnschriftRow
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

    /// <summary>Was eine Person über sich entschieden hat.</summary>
    public DbSet<KontoeinstellungenRow> AccountSettings => Set<KontoeinstellungenRow>();

    /// <summary>Bewerbungsanschrift. Personenzeile.</summary>
    public DbSet<AnschriftRow> Addresses => Set<AnschriftRow>();

    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<InvitationRow> Invitations => Set<InvitationRow>();

    public DbSet<VerificationTokenRow> VerificationTokens => Set<VerificationTokenRow>();

    public DbSet<MembershipRow> Memberships => Set<MembershipRow>();

    public DbSet<SessionCapacityRow> SessionCapacities => Set<SessionCapacityRow>();

    public DbSet<GirderRefreshToken> RefreshTokens => Set<GirderRefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<UserRow>(entity =>
        {
            // Bis zur Migration besass Alembic diese Tabelle; die Abbildung trug
            // dafuer `ExcludeFromMigrations()`. Mit dem Python-Dienst faellt die
            // Ausnahme weg: dieser Dienst legt sein Schema jetzt selbst an, wie
            // die anderen zehn auch.
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
            entity.Property(row => row.Language).HasColumnName("language");
            entity.Property(row => row.Berufsfeld).HasColumnName("berufsfeld");
            entity.Property(row => row.CreatedAt).HasColumnName("created_at");
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at");
            entity.Property(row => row.Version).HasColumnName("version").IsConcurrencyToken();
        });

        modelBuilder.Entity<KontoeinstellungenRow>(entity =>
        {
            entity.ToTable("account_settings");
            // `Personenzeile`: der Schlüssel IST die Person. Ohne diese
            // Anmerkung fände der Löschwächter die Tabelle nicht — er sucht
            // nach Spaltennamen UND nach dieser Anmerkung, genau weil solche
            // Tabellen keine `subject_id` neben dem Schlüssel haben.
            entity.HasAnnotation(Personenzeile.Anmerkung, true);
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

        modelBuilder.Entity<AnschriftRow>(entity =>
        {
            entity.ToTable("addresses");
            entity.HasAnnotation(Personenzeile.Anmerkung, true);
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
