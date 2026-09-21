using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>mandates</c> — vier Werte einer Person.</summary>
/// <remarks>
/// <para><strong>Was hier NICHT steht, ist der Inhalt dieser Tabelle.</strong>
/// None Sichtbarkeit, keine Release, kein Schalter „für alle" — nichts davon
/// gehört einem Dienst, der nicht der Ledger ist (ADR-0020). Ein Test hält
/// fest, dass keine Spalte dieses Modells <c>sichtbar</c>, <c>visible</c>,
/// <c>public</c> oder <c>freigabe</c> im Namen trägt, und das ist kein
/// Namensstreit: eine solche Spalte wäre die zweite Wahrheit, und sie liefe
/// beim ersten Revocation auseinander.</para>
///
/// <para>Und keine Verfügbarkeit: die steht im MarketStatus bei
/// transfer-service, samt der abgeleiteten Aussage „ansprechbar". Sie hier zu
/// spiegeln hieße, eine Aussage zu halten, die jemanden den Arbeitsplatz kosten
/// kann, an einer Job, an der sie niemand pflegt.</para>
/// </remarks>
public sealed class MandateRow
{
    /// <summary>Die Subjekt-Kennung. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    /// <summary>Monat <c>JJJJ-MM</c>, oder <c>null</c>.</summary>
    public string? EntryMonth { get; set; }

    /// <summary>Euro im Monat, oder <c>null</c>.</summary>
    public int? SalaryMin { get; set; }

    /// <summary>Euro im Monat, oder <c>null</c>.</summary>
    public int? SalaryMax { get; set; }

    /// <summary>10–100, oder <c>null</c>.</summary>
    public int? WorkloadPercent { get; set; }

    /// <summary>Die ausgeschlossenen Domains, als jsonb.</summary>
    /// <remarks>
    /// Eine Spalte und keine zweite Tabelle: sie wird immer vollständig gelesen
    /// und vollständig ersetzt, es gibt keine Abfrage über eine einzelne
    /// Domain, und eine eigene Tabelle brächte nur eine Verknüpfung und eine
    /// Reihenfolge, die niemand braucht.
    /// </remarks>
    public List<string> ExcludedDomains { get; set; } = [];

    /// <summary>Wann zuletzt geschrieben.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Eine Zeile von <c>conversations</c>.</summary>
/// <remarks>
/// <strong>None Stufenspalte.</strong> Nicht <c>stage</c>, nicht
/// <c>has_stage</c>, nicht <c>released_at</c>: die Stufe wird bei jedem Lesen
/// aus dem Ledger geholt (ADR-0037 Entscheidung 2). Eine Spalte hier wäre die
/// Stufe als zweite Tür neben dem Ledger — und ein Revocation müsste dann an zwei
/// Jobs wirken.
/// <para>
/// Und keine Nachrichtentabelle: ADR-0037 Entscheidung 5 lässt offen, ob
/// Menschen hier tippen. Solange das offen ist, gibt es keine Tabelle dafür.
/// </para>
/// </remarks>
public sealed class ConversationRow
{
    /// <summary>Welches Gespräch.</summary>
    public Guid Id { get; set; }

    /// <summary>Mit wem — die Personenspalte dieses Dienstes.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>Welches Unternehmen.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Wo es steht.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Was beim Eröffnen geschrieben wurde.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Wann es begann.</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>Wann sich zuletzt etwas änderte.</summary>
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die zwei eigenen Tabellen dieses Dienstes — und der Postausgang.</summary>
/// <remarks>
/// Verfolgen ist aus, als Voreinstellung des Kontexts und nicht je Abfrage. Je
/// Abfrage wäre es Disziplin, und die nächste hinzugefügte vergisst es. Ein
/// änderndes <c>SaveAsync</c> nimmt sich sein <c>AsTracking()</c> deshalb
/// ausdrücklich.
/// </remarks>
public sealed class AdvisorDbContext(DbContextOptions<AdvisorDbContext> options) : DbContext(options)
{
    /// <summary>Die Mandate.</summary>
    public DbSet<MandateRow> Mandate => Set<MandateRow>();

    /// <summary>Die Gespräche.</summary>
    public DbSet<ConversationRow> Gespraeche => Set<ConversationRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<MandateRow>(row =>
        {
            row.ToTable("mandates");
            // KEINE zweite Spalte fuer die Person. Der Schluessel IST sie, und
            // `conversations` in derselben Datei fuehrt daneben eine eigene
            // `subject_id` — wer die beiden vergleicht, haelt das hier fuer
            // vergessen und traegt die Spalte nach. Dann stuenden zwei Angaben
            // ueber denselben Menschen nebeneinander, und die Erasure (ADR-0027)
            // traefe die eine und liesse die andere stehen. Die Anmerkung sagt es
            // stattdessen aus: sie ist das, was `LoeschempfaengerTests` liest, wo
            // kein Spaltenname zu finden ist.
            row.HasAnnotation(SubjectKeyedTable.Anmerkung, true);
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.EntryMonth)
                .HasColumnName("entry_month").HasMaxLength(7);
            row.Property(entry => entry.SalaryMin).HasColumnName("salary_min");
            row.Property(entry => entry.SalaryMax).HasColumnName("salary_max");
            row.Property(entry => entry.WorkloadPercent).HasColumnName("workload_percent");
            row.Property(entry => entry.ExcludedDomains)
                .HasColumnName("excluded_domains").HasColumnType("jsonb").IsRequired();
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<ConversationRow>(row =>
        {
            row.ToTable("conversations");
            row.HasKey(entry => entry.Id);
            row.Property(entry => entry.Id).HasColumnName("id").ValueGeneratedNever();
            row.Property(entry => entry.SubjectId).HasColumnName("subject_id").IsRequired();
            row.Property(entry => entry.TenantId).HasColumnName("tenant_id").IsRequired();
            row.Property(entry => entry.State)
                .HasColumnName("state").HasMaxLength(16).IsRequired();
            row.Property(entry => entry.Note)
                .HasColumnName("note").HasMaxLength(2000).IsRequired();
            row.Property(entry => entry.OpenedAt).HasColumnName("opened_at").IsRequired();
            row.Property(entry => entry.UpdatedAt).HasColumnName("updated_at").IsRequired();

            // Die zwei Abfragen, die es gibt: meine Gespraeche, und unsere.
            row.HasIndex(entry => entry.SubjectId);
            row.HasIndex(entry => entry.TenantId);
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }
}
