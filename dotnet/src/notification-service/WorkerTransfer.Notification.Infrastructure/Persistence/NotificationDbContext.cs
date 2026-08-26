using Microsoft.EntityFrameworkCore;

namespace WorkerTransfer.Notification.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>notification_preferences</c>.</summary>
public sealed class WunschZeile
{
    /// <summary>Die Subjekt-Kennung. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Die Schalter, als jsonb.</summary>
    /// <remarks>
    /// Als eine Spalte und nicht als vier: die Arten werden nur zusammen
    /// gelesen und geschrieben, es gibt keine Abfrage über eine einzelne, und
    /// eine fünfte Art hieße sonst eine Wanderung für ein Feld.
    /// </remarks>
    public string Kinds { get; set; } = "{}";

    /// <summary>Wann zuletzt etwas hinausging.</summary>
    public DateTime? LastSentAt { get; set; }
}

/// <summary>Eine Zeile von <c>notifications</c>.</summary>
/// <remarks>
/// <strong>Keine Inhaltsspalte.</strong> Was hier steht, landet in jeder
/// Sicherung — und eine Spalte für den Text wäre die Einladung, hineinzuschreiben,
/// worum es geht.
/// </remarks>
public sealed class EingangsZeile
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }
}

/// <summary>Die zwei Tabellen, die dieser Dienst besitzt.</summary>
/// <remarks>
/// <strong>Keine Outbox.</strong> Dieser Dienst ist der Empfänger von
/// Absichten, nicht ihr Absender; eine Outbox hier wäre eine zweite Warteschlange
/// hinter der ersten. Und keine Adressspalte — er kennt keine E-Mail-Adresse
/// und soll keine kennen.
/// </remarks>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Einstellungen.</summary>
    public DbSet<WunschZeile> Wuensche => Set<WunschZeile>();

    /// <summary>Die Postfächer.</summary>
    public DbSet<EingangsZeile> Eingaenge => Set<EingangsZeile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<WunschZeile>(entity =>
        {
            entity.ToTable("notification_preferences");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.Kinds)
                .HasColumnName("kinds").HasColumnType("jsonb").IsRequired();
            entity.Property(zeile => zeile.LastSentAt).HasColumnName("last_sent_at");
        });

        modelBuilder.Entity<EingangsZeile>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(zeile => zeile.Kind)
                .HasColumnName("kind").HasMaxLength(32).IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.ReadAt).HasColumnName("read_at");

            entity.HasIndex(zeile => new { zeile.UserId, zeile.CreatedAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
