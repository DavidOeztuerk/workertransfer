using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>github_connections</c>.</summary>
/// <remarks>
/// Der Abzug liegt als jsonb daneben, nicht in einer eigenen Tabelle: er wird
/// immer als Ganzes geschrieben und als Ganzes gelesen, nie einzeln abgefragt.
/// Eine zweite Tabelle wäre ein Verbund für einen Wert, der nur zusammen Sinn
/// ergibt — und eine Zeile je Repository wäre die Einladung, danach zu
/// sortieren.
/// </remarks>
public sealed class ConnectionRow
{
    /// <summary>Die Subjekt-Kennung. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary><c>null</c>: noch kein Konto genannt.</summary>
    public string? Login { get; set; }

    public string Challenge { get; set; } = string.Empty;

    public DateTime? VerifiedAt { get; set; }

    public DateTime? FetchedAt { get; set; }

    /// <summary>Der Abzug, als jsonb.</summary>
    public string Repositories { get; set; } = "[]";

    /// <summary>Konnten fuer jedes Repository die Sprachen geholt werden?</summary>
    public bool LanguagesComplete { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die eine Tabelle, die dieser Dienst besitzt.</summary>
/// <remarks>
/// None Outbox: dieser Dienst verschickt nichts. Und keine Spalte, die einen
/// Menschen zusammenfasst — kein Punktwert, kein Rang, keine abgeleitete
/// Fähigkeit (ADR-0022).
/// </remarks>
public sealed class GitHubDbContext(DbContextOptions<GitHubDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Connections.</summary>
    public DbSet<ConnectionRow> Connections => Set<ConnectionRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ConnectionRow>(entity =>
        {
            entity.ToTable("github_connections");
            entity.HasAnnotation(SubjectKeyedTable.Anmerkung, true);
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            // OHNE `IsRequired`: eine Connection, die über GitHubs Anmeldung
            // entsteht, kennt ihr Konto noch nicht — den Namen meldet GitHub.
            // Eine leere Zeichenkette statt `null` wäre ein Name, den es nicht
            // gibt, und würde in jeder Abfrage mitgezählt.
            entity.Property(row => row.Login)
                .HasColumnName("login")
                .HasMaxLength(Connection.HoechstlaengeLogin);
            entity.Property(row => row.Challenge)
                .HasColumnName("challenge").HasMaxLength(64).IsRequired();
            entity.Property(row => row.VerifiedAt).HasColumnName("verified_at");
            entity.Property(row => row.FetchedAt).HasColumnName("fetched_at");
            entity.Property(row => row.Repositories)
                .HasColumnName("repositories").HasColumnType("jsonb").IsRequired();
            // Vorgabe `true`: bestehende Zeilen stammen aus einer Zeit, in der
            // ohne Token gar keine Sprachen geholt wurden. `false` zu setzen
            // waere ehrlicher fuer die Vergangenheit und laestiger fuer die
            // Gegenwart — der naechste Abruf schreibt ohnehin den wahren Wert,
            // und bis dahin behauptet die Zeile nichts, was sie nicht belegen
            // koennte: sie zeigt schlicht, was sie hat.
            entity.Property(row => row.LanguagesComplete)
                .HasColumnName("languages_complete").HasDefaultValue(true).IsRequired();
            entity.Property(row => row.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(row => row.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
