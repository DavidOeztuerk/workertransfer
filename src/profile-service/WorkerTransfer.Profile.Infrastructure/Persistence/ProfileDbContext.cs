using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Pruefspur;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Eine Zeile <c>profiles</c>, wie sie in der Datenbank steht.</summary>
/// <remarks>
/// Nicht das Aggregat. <see cref="EfProfilspeicher"/> baut daraus ein
/// <see cref="Domain.Profile.Profil"/>, damit nichts, was am Aggregat geschieht,
/// von selbst in der Tabelle landet.
/// <para>
/// <b>Keine Spalte für Sichtbarkeit</b> (ADR-0020). Wer hier eine anlegt, hat
/// eine zweite Wahrheit angelegt — und die eine, die man vergisst
/// mitzuändern.
/// </para>
/// </remarks>
public sealed class ProfilZeile
{
    /// <summary>Zugleich die Kennung der Person: ein Profil je Mensch.</summary>
    public Guid Id { get; set; }

    /// <summary>Die Spalte <c>headline</c>.</summary>
    public string Ueberschrift { get; set; } = string.Empty;

    /// <summary>Die Spalte <c>bio</c>.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Die Spalte <c>location</c>.</summary>
    public string Ort { get; set; } = string.Empty;

    /// <summary>Die Spalte <c>remote_ok</c>.</summary>
    public bool RemoteMoeglich { get; set; }

    /// <summary>
    /// Die Spalte <c>skills</c> als <c>text[]</c>.
    /// </summary>
    /// <remarks>
    /// Ein echtes Feld und kein JSON-Dokument: Postgres kann darüber suchen und
    /// indizieren, ohne dass jemand eine zweite, kleingeschriebene Kopie
    /// derselben Wörter pflegt.
    /// </remarks>
    public string[] Faehigkeiten { get; set; } = [];

    /// <summary>
    /// Die Spalte <c>commute_km</c> — eine Stufe, <c>null</c> für „nichts gesagt".
    /// </summary>
    /// <remarks>
    /// <strong>Als Text und nicht als Zahl</strong>, obwohl der Name eine Zahl
    /// nahelegt. Eine Zahl in der Spalte wäre die Einladung, sie zu vergleichen
    /// („WHERE commute_km >= 37") und irgendwann danach zu sortieren — genau
    /// das, was ADR-0041 nicht will. Die Stufe ist eine Aussage, kein Messwert.
    /// </remarks>
    public Pendelbereitschaft? Pendelbereitschaft { get; set; }

    /// <summary>Die Spalte <c>relocation</c>, <c>null</c> für „nichts gesagt".</summary>
    public Umzugsbereitschaft? Umzugsbereitschaft { get; set; }

    /// <summary>Die Spalte <c>created_at</c>.</summary>
    public DateTimeOffset AngelegtAm { get; set; }

    /// <summary>Die Spalte <c>updated_at</c>. Zugleich die halbe Sortierung.</summary>
    public DateTimeOffset GeaendertAm { get; set; }
}

/// <summary>Eine Zeile <c>audit_events</c>.</summary>
/// <remarks>
/// Die Spur wird nur angehängt, also gibt es kein Aggregat, das daraus wieder
/// entstünde. <c>action</c> ist die Aufzählung <c>pruef_handlung</c>, gespeichert
/// als die Bezeichnung, die Npgsql daraus macht.
/// </remarks>
public sealed class Pruefzeile
{
    /// <summary>Die Kennung der Zeile.</summary>
    public Guid Id { get; set; }

    /// <summary>Wer gehandelt hat, oder <c>null</c> bei der Löschkaskade.</summary>
    public Guid? AkteurId { get; set; }

    /// <summary>Für welches Unternehmen, oder <c>null</c>.</summary>
    public Guid? FirmaId { get; set; }

    /// <summary>Was geschehen ist.</summary>
    public Pruefhandlung Handlung { get; set; }

    /// <summary>An wem.</summary>
    public Guid? BetroffenId { get; set; }

    /// <summary>Die Anfrage, über Dienstgrenzen hinweg.</summary>
    public string? Korrelation { get; set; }

    /// <summary>Wann.</summary>
    public DateTimeOffset GeschehenAm { get; set; }

    /// <summary>Technisches Beiwerk als JSON, nur von der Liste.</summary>
    public string Daten { get; set; } = "{}";
}

/// <summary>Die Tabellen dieses Dienstes — und sie gehören ihm allein.</summary>
/// <remarks>
/// Kein <c>ExcludeFromMigrations</c> irgendwo: anders als bei identity legt
/// hier keine fremde Werkzeugkette die Tabellen an. Das Schema entsteht aus
/// diesen Migrationen, und der Testcontainer bekommt es über
/// <c>Database.MigrateAsync()</c> — dieselbe Quelle wie der Betrieb.
/// <para>
/// Die Verfolgung ist für den ganzen Kontext aus. Je Abfrage wäre sie Disziplin,
/// und die nächste Abfrage, die jemand hinzufügt, vergisst sie —
/// <see cref="EfProfilspeicher"/> schickt gespeicherte Werte durch die Domäne
/// zurück, die sie normalisiert, und eine verfolgte Zeile stünde danach als
/// geändert da.
/// </para>
/// </remarks>
public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    /// <summary>Die Profile.</summary>
    public DbSet<ProfilZeile> Profile => Set<ProfilZeile>();

    /// <summary>Die Prüfspur.</summary>
    public DbSet<Pruefzeile> Pruefeintraege => Set<Pruefzeile>();

    /// <summary>Die Namen der Postgres-Aufzählungen dieses Dienstes.</summary>
    /// <remarks>
    /// Als Konstante und nicht als Zeichenkette an der Aufrufstelle: der Name
    /// steht in der Datenbank und in der Wanderung, und zwei Schreibweisen
    /// desselben Namens fallen erst beim ersten Einfügen auf.
    /// </remarks>
    public static class Enumnamen
    {
        /// <summary>Hinter <c>audit_events.action</c>.</summary>
        public const string Pruefhandlung = "pruef_handlung";
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // `name:` ausgeschrieben, und das ist keine Zierde: die generische
        // Überladung nimmt ihr ERSTES Argument als SCHEMA, nicht als Typnamen.
        // Positionell geschrieben legte diese Zeile ein Schema `pruef_handlung`
        // an und darin einen Typ `pruefhandlung` — eine Waise, die niemand
        // benutzt, denn die Wanderung hatte den echten Typ längst als
        // `public.pruef_handlung` angelegt. Gemessen an der laufenden Datenbank:
        //
        //   nspname        | typname
        //   public         | pruef_handlung     <- die benutzte
        //   pruef_handlung | pruefhandlung      <- die Waise
        //
        // Folgenlos im Betrieb, aber sie entsteht bei JEDER Wanderung in JEDER
        // Umgebung neu. resume-service schreibt es seit jeher benannt; nur
        // dieser Dienst tat es nicht.
        modelBuilder.HasPostgresEnum<Pruefhandlung>(name: Enumnamen.Pruefhandlung);

        modelBuilder.Entity<ProfilZeile>(zeile =>
        {
            zeile.ToTable("profiles");
            zeile.HasAnnotation(Personenzeile.Anmerkung, true);
            zeile.HasKey(spalte => spalte.Id);
            zeile.Property(spalte => spalte.Id).HasColumnName("id");
            zeile.Property(spalte => spalte.Ueberschrift).HasColumnName("headline");
            zeile.Property(spalte => spalte.Text).HasColumnName("bio");
            zeile.Property(spalte => spalte.Ort).HasColumnName("location");
            zeile.Property(spalte => spalte.RemoteMoeglich).HasColumnName("remote_ok");
            zeile.Property(spalte => spalte.Faehigkeiten).HasColumnName("skills");
            // Als Zeichenkette und nicht als Postgres-Aufzaehlung: die beiden
            // sind freiwillig und nullbar, und eine Aufzaehlung braechte je
            // Erweiterung eine Typwanderung — fuer eine Angabe, die niemand
            // abfragt, sondern die zur Suchzeit in ein Haekchen wandert.
            zeile.Property(spalte => spalte.Pendelbereitschaft)
                .HasColumnName("commute_km").HasMaxLength(16).HasConversion<string>();
            zeile.Property(spalte => spalte.Umzugsbereitschaft)
                .HasColumnName("relocation").HasMaxLength(16).HasConversion<string>();
            zeile.Property(spalte => spalte.AngelegtAm).HasColumnName("created_at");
            zeile.Property(spalte => spalte.GeaendertAm).HasColumnName("updated_at");

            // Genau die Sortierung der Seite, in genau der Richtung. Ohne ihn
            // liest jede Seite die ganze Tabelle — und die Sortierung ist
            // zusammengesetzt, ein Index auf updated_at allein hülfe nicht.
            zeile.HasIndex(spalte => new { spalte.GeaendertAm, spalte.Id })
                .IsDescending(true, true)
                .HasDatabaseName("ix_profiles_seite");
        });

        modelBuilder.Entity<Pruefzeile>(zeile =>
        {
            zeile.ToTable("audit_events");
            zeile.HasKey(spalte => spalte.Id);
            zeile.Property(spalte => spalte.Id).HasColumnName("id");
            zeile.Property(spalte => spalte.AkteurId).HasColumnName("actor_id");
            zeile.Property(spalte => spalte.FirmaId).HasColumnName("tenant_id");
            zeile.Property(spalte => spalte.Handlung).HasColumnName("action");
            zeile.Property(spalte => spalte.BetroffenId).HasColumnName("target_id");
            zeile.Property(spalte => spalte.Korrelation).HasColumnName("correlation_id");
            zeile.Property(spalte => spalte.GeschehenAm).HasColumnName("occurred_at");
            zeile.Property(spalte => spalte.Daten).HasColumnName("metadata").HasColumnType("jsonb");

            // Ausdrücklich KEIN Fremdschlüssel auf profiles: die Löschung
            // entfernt die Profilzeile und behält die Spur. Ein Fremdschlüssel
            // hieße hier entweder Kaskade — dann verschwände der Beweis, dass
            // gelöscht wurde — oder Verbot, dann ginge die Löschung nicht.
        });

        base.OnModelCreating(modelBuilder);
    }
}
