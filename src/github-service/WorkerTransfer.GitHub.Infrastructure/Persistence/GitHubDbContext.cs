using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>github_connections</c>.</summary>
/// <remarks>
/// Der Abzug liegt als jsonb daneben, nicht in einer eigenen Tabelle: er wird
/// immer als Ganzes geschrieben und als Ganzes gelesen, nie einzeln abgefragt.
/// Eine zweite Tabelle wäre ein Verbund für einen Wert, der nur zusammen Sinn
/// ergibt — und eine Zeile je Repository wäre die Einladung, danach zu
/// sortieren.
/// </remarks>
public sealed class VerbindungsZeile
{
    /// <summary>Die Subjekt-Kennung. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    public string Login { get; set; } = string.Empty;

    public string Challenge { get; set; } = string.Empty;

    public DateTime? VerifiedAt { get; set; }

    public DateTime? FetchedAt { get; set; }

    /// <summary>Der Abzug, als jsonb.</summary>
    public string Repositories { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die eine Tabelle, die dieser Dienst besitzt.</summary>
/// <remarks>
/// Keine Outbox: dieser Dienst verschickt nichts. Und keine Spalte, die einen
/// Menschen zusammenfasst — kein Punktwert, kein Rang, keine abgeleitete
/// Fähigkeit (ADR-0022).
/// </remarks>
public sealed class GitHubDbContext(DbContextOptions<GitHubDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Verbindungen.</summary>
    public DbSet<VerbindungsZeile> Verbindungen => Set<VerbindungsZeile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<VerbindungsZeile>(entity =>
        {
            entity.ToTable("github_connections");
            // Der Schluessel IST der Mensch: diese Zeile heisst ihre
            // `subject_id` schlicht `id`. Ausgesprochen, weil der
            // Loeschwaechter (`LoeschempfaengerTests`) sonst nur nach den
            // Spaltennamen `subject_id`/`user_id` sucht und diese Tabelle
            // uebersaehe — und damit die Zusage aus ADR-0027 fuer einen ganzen
            // Dienst.
            entity.HasAnnotation(Personenzeile.Anmerkung, true);
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.Login)
                .HasColumnName("login")
                .HasMaxLength(Verbindung.HoechstlaengeLogin)
                .IsRequired();
            entity.Property(zeile => zeile.Challenge)
                .HasColumnName("challenge").HasMaxLength(64).IsRequired();
            entity.Property(zeile => zeile.VerifiedAt).HasColumnName("verified_at");
            entity.Property(zeile => zeile.FetchedAt).HasColumnName("fetched_at");
            entity.Property(zeile => zeile.Repositories)
                .HasColumnName("repositories").HasColumnType("jsonb").IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
