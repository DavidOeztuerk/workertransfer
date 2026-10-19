using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Infrastructure.Persistence;

/// <summary>One row of <c>notification_preferences</c>.</summary>
public sealed class PreferenceRow
{
    /// <summary>The subject id. It <em>is</em> the key.</summary>
    public Guid Id { get; set; }

    /// <summary>The switches, as jsonb.</summary>
    /// <remarks>
    /// As one column and not as eight: the kinds are only read and written
    /// together, there is no query over a single one, and a ninth kind would
    /// otherwise mean a migration for one field.
    /// </remarks>
    public string Kinds { get; set; } = "{}";

    /// <summary>When something last went out.</summary>
    public DateTime? LastSentAt { get; set; }
}

/// <summary>One row of <c>notifications</c>.</summary>
/// <remarks>
/// <strong>No content column.</strong> What stands here ends up in every
/// backup — and a column for the text would be the invitation to write into it
/// what the matter is.
/// </remarks>
public sealed class InboxRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }
}

/// <summary>The two tables this service owns.</summary>
/// <remarks>
/// <strong>No outbox.</strong> This service is the recipient of intents, not
/// their sender; an outbox here would be a second queue behind the first. And
/// no address column — it knows no email address and is meant to know none.
/// </remarks>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    /// <summary>The settings.</summary>
    public DbSet<PreferenceRow> Preferences => Set<PreferenceRow>();

    /// <summary>The inboxes.</summary>
    public DbSet<InboxRow> InboxEntries => Set<InboxRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PreferenceRow>(entity =>
        {
            entity.ToTable("notification_preferences");
            // NO second column for the person. The key IS them, and
            // `notifications` in the same file carries a `user_id` of its own
            // beside it — whoever compares the two takes this for forgotten and
            // adds the column. Then two statements about the same human being
            // would stand side by side, and the erasure (ADR-0027) would strike
            // the one and leave the other. The annotation says it instead: it
            // is what `ErasureRecipientTests` reads where no column name is to
            // be found.
            entity.HasAnnotation(SubjectKeyedTable.Annotation, true);
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Kinds)
                .HasColumnName("kinds").HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.LastSentAt).HasColumnName("last_sent_at");
        });

        modelBuilder.Entity<InboxRow>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(row => row.Kind)
                .HasColumnName("kind").HasMaxLength(32).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.ReadAt).HasColumnName("read_at");

            entity.HasIndex(row => new { row.UserId, row.CreatedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
