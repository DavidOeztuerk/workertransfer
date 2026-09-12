using Microsoft.EntityFrameworkCore;

namespace WorkerTransfer.Outbox;

/// <summary>One intent, as a row.</summary>
/// <remarks>
/// Deliberately narrow: an addressee, a kind, timestamps, a counter. **No text
/// column that a message could later slip into.** An outbox is durable storage
/// — what gets in here is afterwards in a table, in a backup and in every dump.
/// Free text somebody wrote about themselves, message bodies and CVs have no
/// business there (ADR-0025).
/// <para>
/// The payload is a <em>reference</em>, never a document: the recipient reads
/// the current state itself. That is also why a redelivery is harmless.
/// </para>
/// </remarks>
public sealed class OutboxZeile
{
    /// <summary>Which intent.</summary>
    public Guid Id { get; set; }

    /// <summary>Whom it is about.</summary>
    public Guid UserId { get; set; }

    /// <summary>What is meant, e.g. <c>transfer.accepted</c> or <c>erasure:consent</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// When the intent was recorded. Oldest first, because a notification that
    /// gets overtaken arrives in the wrong order.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>How often delivery was tried.</summary>
    public int Attempts { get; set; }

    /// <summary><c>null</c> means outstanding. The column the dispatcher searches on.</summary>
    public DateTime? DeliveredAt { get; set; }

    /// <summary>
    /// Only the <em>kind</em> of failure (<c>HttpRequestException</c>), never
    /// the other side's answer and never any content.
    /// </summary>
    public string LastError { get; set; } = string.Empty;
}

/// <summary>Maps the outbox table into a service's own context.</summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>The default table name.</summary>
    public const string Tabelle = "outbox";

    /// <summary>
    /// Adds the table. Call it from <c>OnModelCreating</c>.
    /// </summary>
    /// <remarks>
    /// A method rather than a shared entity in a shared database: there is no
    /// shared database (ADR-0004), so there is no shared outbox. Every service
    /// hangs it into <em>its</em> context so it shows up in <em>its</em>
    /// migrations — and so the row commits with the change that caused it.
    /// </remarks>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="tabelle">Rename it where a service's conventions differ.</param>
    public static ModelBuilder ConfigureOutbox(
        this ModelBuilder modelBuilder,
        string tabelle = Tabelle)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxZeile>(entity =>
        {
            entity.ToTable(tabelle);
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id");
            entity.Property(zeile => zeile.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(zeile => zeile.Kind)
                .HasColumnName("kind").HasMaxLength(64).IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.Attempts).HasColumnName("attempts").IsRequired();
            entity.Property(zeile => zeile.DeliveredAt).HasColumnName("delivered_at");
            entity.Property(zeile => zeile.LastError)
                .HasColumnName("last_error").HasMaxLength(120).IsRequired();

            // Both are the dispatcher's search, on every tick.
            entity.HasIndex(zeile => zeile.CreatedAt);
            entity.HasIndex(zeile => zeile.DeliveredAt);
        });

        return modelBuilder;
    }
}
