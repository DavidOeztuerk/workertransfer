using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>searches</c> — eine gespeicherte ResumeRequest.</summary>
/// <remarks>
/// <para><strong>Was hier NICHT steht, ist der Inhalt dieser Tabelle.</strong>
/// Kein Hit, keine Kennung eines gefundenen Menschen, kein Zeitpunkt eines
/// Laufs, keine Zahl. Ein gespeichertes Ergebnis über Menschen veraltet gegen
/// einen Revocation, und ein Revocation muss beim nächsten Aufruf wirken
/// (ADR-0013, ADR-0036 Entscheidung 4).</para>
///
/// <para>Die Worte liegen als <c>jsonb</c> in einer Spalte und nicht in einer
/// zweiten Tabelle: sie werden immer vollständig gelesen und vollständig
/// ersetzt, es gibt keine Abfrage über ein einzelnes Wort, und eine eigene
/// Tabelle brächte nur eine Verknüpfung und eine Reihenfolge, die niemand
/// braucht.</para>
/// </remarks>
public sealed class SearchRow
{
    /// <summary>Welche Search.</summary>
    public Guid Id { get; set; }

    /// <summary>Für welches Unternehmen.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Wer sie abgelegt hat — die Personenspalte dieses Dienstes.
    /// </summary>
    /// <remarks>
    /// An ihr erkennt <c>LoeschempfaengerTests</c> diesen Dienst als
    /// Löschempfänger (ADR-0027 §4). Ab dieser einen Spalte gehört
    /// <c>"scout"</c> in <c>ErasureRecipient.Fremde</c>.
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
/// es. Ein änderndes <c>SaveAsync</c> nimmt sich sein
/// <c>AsTracking()</c> deshalb ausdrücklich.</para>
///
/// <para><strong>None Profiltabelle, keine Treffertabelle, keine Tabelle „wer
/// hat wen angesehen".</strong> Die erste wäre eine Kopie fremder Daten
/// (ADR-0004), die zweite der verbotene Zwischenspeicher (ADR-0013), die dritte
/// die sensibelste Tabelle des Systems (ADR-0033).</para>
/// </remarks>
public sealed class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : DbContext(options)
{
    /// <summary>Die gespeicherten Anfragen.</summary>
    public DbSet<SearchRow> Searches => Set<SearchRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<SearchRow>(row =>
        {
            row.ToTable("searches");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id").IsRequired();
            row.Property(entry => entry.SubjectId).HasColumnName("subject_id").IsRequired();
            row.Property(entry => entry.Name)
                .HasColumnName("name").HasMaxLength(80).IsRequired();
            row.Property(entry => entry.Skills)
                .HasColumnName("skills").HasColumnType("jsonb").IsRequired();
            row.Property(entry => entry.Location)
                .HasColumnName("location").HasMaxLength(120).IsRequired();
            row.Property(entry => entry.Remote).HasColumnName("remote").IsRequired();
            row.Property(entry => entry.CreatedAt).HasColumnName("created_at").IsRequired();

            // Die eine Abfrage, die es gibt: die Searches EINES Menschen in EINEM
            // Unternehmen.
            row.HasIndex(entry => new { entry.TenantId, entry.SubjectId });
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
