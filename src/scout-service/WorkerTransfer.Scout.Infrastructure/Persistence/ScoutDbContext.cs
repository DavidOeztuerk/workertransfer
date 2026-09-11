using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>searches</c> — eine gespeicherte Anfrage.</summary>
/// <remarks>
/// <para><strong>Was hier NICHT steht, ist der Inhalt dieser Tabelle.</strong>
/// Kein Treffer, keine Kennung eines gefundenen Menschen, kein Zeitpunkt eines
/// Laufs, keine Zahl. Ein gespeichertes Ergebnis über Menschen veraltet gegen
/// einen Widerruf, und ein Widerruf muss beim nächsten Aufruf wirken
/// (ADR-0013, ADR-0036 Entscheidung 4).</para>
///
/// <para>Die Worte liegen als <c>jsonb</c> in einer Spalte und nicht in einer
/// zweiten Tabelle: sie werden immer vollständig gelesen und vollständig
/// ersetzt, es gibt keine Abfrage über ein einzelnes Wort, und eine eigene
/// Tabelle brächte nur eine Verknüpfung und eine Reihenfolge, die niemand
/// braucht.</para>
/// </remarks>
public sealed class SucheZeile
{
    /// <summary>Welche Suche.</summary>
    public Guid Id { get; set; }

    /// <summary>Für welches Unternehmen.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Wer sie abgelegt hat — die Personenspalte dieses Dienstes.
    /// </summary>
    /// <remarks>
    /// An ihr erkennt <c>LoeschempfaengerTests</c> diesen Dienst als
    /// Löschempfänger (ADR-0027 §4). Ab dieser einen Spalte gehört
    /// <c>"scout"</c> in <c>Loeschempfaenger.Fremde</c>.
    /// </remarks>
    public Guid SubjectId { get; set; }

    /// <summary>Wie die Person sie genannt hat.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Die gesuchten Worte, als jsonb.</summary>
    public List<string> Skills { get; set; } = [];

    /// <summary>Teiltext auf dem Ort.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Nur, wer Remote ausdrücklich angekreuzt hat.</summary>
    public bool Remote { get; set; }

    /// <summary>Wann sie abgelegt wurde.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Die eine eigene Tabelle dieses Dienstes — und der Postausgang.</summary>
/// <remarks>
/// <para>Verfolgen ist aus, als Voreinstellung des Kontexts und nicht je
/// Abfrage. Je Abfrage wäre es Disziplin, und die nächste hinzugefügte vergisst
/// es. Ein änderndes <c>SichereAsync</c> nimmt sich sein
/// <c>AsTracking()</c> deshalb ausdrücklich.</para>
///
/// <para><strong>Keine Profiltabelle, keine Treffertabelle, keine Tabelle „wer
/// hat wen angesehen".</strong> Die erste wäre eine Kopie fremder Daten
/// (ADR-0004), die zweite der verbotene Zwischenspeicher (ADR-0013), die dritte
/// die sensibelste Tabelle des Systems (ADR-0033).</para>
/// </remarks>
public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : DbContext(options)
{
    /// <summary>Die gespeicherten Anfragen.</summary>
    public DbSet<SucheZeile> Suchen => Set<SucheZeile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SucheZeile>(zeile =>
        {
            zeile.ToTable("searches");
            zeile.HasKey(eintrag => eintrag.Id);
            zeile.Property(eintrag => eintrag.Id).HasColumnName("id").ValueGeneratedNever();
            zeile.Property(eintrag => eintrag.TenantId).HasColumnName("tenant_id").IsRequired();
            zeile.Property(eintrag => eintrag.SubjectId).HasColumnName("subject_id").IsRequired();
            zeile.Property(eintrag => eintrag.Name)
                .HasColumnName("name").HasMaxLength(80).IsRequired();
            zeile.Property(eintrag => eintrag.Skills)
                .HasColumnName("skills").HasColumnType("jsonb").IsRequired();
            zeile.Property(eintrag => eintrag.Location)
                .HasColumnName("location").HasMaxLength(120).IsRequired();
            zeile.Property(eintrag => eintrag.Remote).HasColumnName("remote").IsRequired();
            zeile.Property(eintrag => eintrag.CreatedAt).HasColumnName("created_at").IsRequired();

            // Die eine Abfrage, die es gibt: die Suchen EINES Menschen in EINEM
            // Unternehmen.
            zeile.HasIndex(eintrag => new { eintrag.TenantId, eintrag.SubjectId });
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
