using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Messages.Infrastructure.Persistence;

/// <summary>A row of <c>conversations</c> — one per process.</summary>
public sealed class ConversationRow
{
    /// <summary>Which conversation.</summary>
    public Guid Id { get; set; }

    /// <summary>The kind of process.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The process's id in its own service.</summary>
    public Guid Reference { get; set; }

    /// <summary>The natural person — this service's person column.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>The company.</summary>
    public Guid TenantId { get; set; }

    /// <summary>When it was opened.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the last message was sent.</summary>
    public DateTime LastMessageAt { get; set; }
}

/// <summary>A row of <c>messages</c>.</summary>
/// <remarks>
/// <para><strong>No read column.</strong> Neither a timestamp nor a flag:
/// "has the company read my message?" is pressure in both directions and a
/// statement about a human being's behaviour (ADR-0047).</para>
///
/// <para><strong>No prompt and no raw answer.</strong> What a model received
/// stands nowhere; the draft is the document a human being checks, and that
/// is all that is kept.</para>
/// </remarks>
public sealed class MessageRow
{
    /// <summary>Which message.</summary>
    public Guid Id { get; set; }

    /// <summary>In which conversation.</summary>
    public Guid ConversationId { get; set; }

    /// <summary><c>person</c> or <c>company</c>.</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>Who released it or asked for the draft; <c>null</c> after their erasure.</summary>
    public Guid? AuthorId { get; set; }

    /// <summary>The words.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary><c>human</c> or <c>agent</c>.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Whether it still waits for release.</summary>
    public bool IsDraft { get; set; }

    /// <summary>When it came into being.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When it was sent.</summary>
    public DateTime? SentAt { get; set; }
}

/// <summary>Conversations, messages, the outbox and the trail.</summary>
/// <remarks>
/// Tracking is off, as the context's default and not per query. A mutating
/// <c>SaveAsync</c> therefore takes its <c>AsTracking()</c> explicitly.
/// </remarks>
public sealed class MessagesDbContext(DbContextOptions<MessagesDbContext> options)
    : DbContext(options)
{
    /// <summary>The audit trail. Survives the erasure it records.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    /// <summary>The conversations.</summary>
    public DbSet<ConversationRow> Conversations => Set<ConversationRow>();

    /// <summary>The messages.</summary>
    public DbSet<MessageRow> Messages => Set<MessageRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ConversationRow>(row =>
        {
            row.ToTable("conversations");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.Kind).HasColumnName("kind").HasMaxLength(32).IsRequired();
            row.Property(entry => entry.Reference).HasColumnName("reference").IsRequired();
            row.Property(entry => entry.SubjectId).HasColumnName("subject_id").IsRequired();
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id").IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();
            row.Property(entry => entry.LastMessageAt).HasColumnName("last_message_at").IsRequired();

            // One conversation per process — as a constraint, not as a habit of
            // the handler: two first messages at once must not open two.
            row.HasIndex(entry => new { entry.Kind, entry.Reference }).IsUnique();
            row.HasIndex(entry => entry.SubjectId);
            row.HasIndex(entry => entry.TenantId);
        });

        modelBuilder.Entity<MessageRow>(row =>
        {
            row.ToTable("messages");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.ConversationId).HasColumnName("conversation_id").IsRequired();
            row.Property(entry => entry.Side).HasColumnName("side").HasMaxLength(16).IsRequired();
            row.Property(entry => entry.AuthorId).HasColumnName("author_id");
            row.Property(entry => entry.Text).HasColumnName("text").HasMaxLength(4000).IsRequired();
            row.Property(entry => entry.Origin).HasColumnName("origin").HasMaxLength(16).IsRequired();
            row.Property(entry => entry.IsDraft).HasColumnName("is_draft").IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();
            row.Property(entry => entry.SentAt).HasColumnName("sent_at");

            // The conversation takes its messages with it — the erasure deletes
            // one row and the database the rest, in the same statement.
            row.HasOne<ConversationRow>()
                .WithMany()
                .HasForeignKey(entry => entry.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            row.HasIndex(entry => new { entry.ConversationId, entry.CreatedAt });
            row.HasIndex(entry => entry.AuthorId);
        });

        modelBuilder.ConfigureOutbox();

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
            // them and keeps the trail.
        });

        base.OnModelCreating(modelBuilder);
    }
}
