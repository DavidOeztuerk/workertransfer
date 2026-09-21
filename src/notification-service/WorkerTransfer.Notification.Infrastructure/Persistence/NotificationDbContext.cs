using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>notification_preferences</c>.</summary>
public sealed class PreferenceRow
{
    /// <summary>Die Subjekt-Kennung. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Die Schalter, als jsonb.</summary>
    /// <remarks>
    /// Als eine Spalte und nicht als vier: die Arten werden nur zusammen
    /// gelesen und geschrieben, es gibt keine Abfrage über eine einzelne, und
    /// eine fünfte Art hieße sonst eine SchemaMigration für ein Feld.
    /// </remarks>
    public string Kinds { get; set; } = "{}";

    /// <summary>Wann zuletzt etwas hinausging.</summary>
    public DateTime? LastSentAt { get; set; }
}

/// <summary>Eine Zeile von <c>notifications</c>.</summary>
/// <remarks>
/// <strong>None Inhaltsspalte.</strong> Was hier steht, landet in jeder
/// Sicherung — und eine Spalte für den Text wäre die Einladung, hineinzuschreiben,
/// worum es geht.
/// </remarks>
public sealed class InboxRow
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? ReadAt { get; set; }
}

/// <summary>Die zwei Tabellen, die dieser Dienst besitzt.</summary>
/// <remarks>
/// <strong>None Outbox.</strong> Dieser Dienst ist der Empfänger von
/// Absichten, nicht ihr Absender; eine Outbox hier wäre eine zweite Warteschlange
/// hinter der ersten. Und keine Adressspalte — er kennt keine E-Mail-Adresse
/// und soll keine kennen.
/// </remarks>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Einstellungen.</summary>
    public DbSet<PreferenceRow> Wuensche => Set<PreferenceRow>();

    /// <summary>Die Postfächer.</summary>
    public DbSet<InboxRow> Eingaenge => Set<InboxRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PreferenceRow>(entity =>
        {
            entity.ToTable("notification_preferences");
            // KEINE zweite Spalte fuer die Person. Der Schluessel IST sie, und
            // `notifications` in derselben Datei fuehrt daneben eine eigene
            // `user_id` — wer die beiden vergleicht, haelt das hier fuer vergessen
            // und traegt die Spalte nach. Dann stuenden zwei Angaben ueber
            // denselben Menschen nebeneinander, und die Erasure (ADR-0027)
            // traefe die eine und liesse die andere stehen. Die Anmerkung sagt es
            // stattdessen aus: sie ist das, was `LoeschempfaengerTests` liest, wo
            // kein Spaltenname zu finden ist.
            entity.HasAnnotation(SubjectKeyedTable.Anmerkung, true);
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
