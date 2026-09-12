using Microsoft.EntityFrameworkCore;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Domain.Vorgaenge;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>Eine Zeile von <c>market_status</c>.</summary>
public sealed class MarktZeile
{
    /// <summary>Die Subjekt-Kennung. Sie <em>ist</em> der Schlüssel.</summary>
    public Guid Id { get; set; }

    public string Availability { get; set; } = string.Empty;

    public bool Employed { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Eine Zeile von <c>market_requests</c>.</summary>
/// <remarks>
/// Kein <c>revoked_at</c>: der Widerruf lebt im Ledger. Ihn hier zu spiegeln
/// hieße, zwei Wahrheiten über dieselbe Frage zu führen.
/// </remarks>
public sealed class AnfrageZeile
{
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public Guid TenantId { get; set; }

    /// <summary><c>null</c> heißt: wer gefragt hat, hat sein Konto gelöscht.</summary>
    /// <remarks>
    /// Die Anfrage bleibt — sie gehört dem Unternehmen und handelt von einem
    /// Dritten (ADR-0027 §2). Es heißt <strong>nicht</strong> „niemand hat
    /// gefragt".
    /// </remarks>
    public Guid? RequestedBy { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? AnsweredAt { get; set; }
}

/// <summary>Eine Zeile von <c>transfers</c>.</summary>
public sealed class VorgangsZeile
{
    public Guid Id { get; set; }

    public Guid SubjectId { get; set; }

    public Guid TenantId { get; set; }

    public string Status { get; set; } = string.Empty;

    public bool RequiresRelease { get; set; }

    public bool ReleaseConfirmed { get; set; }

    public string Message { get; set; } = string.Empty;

    public string OfferNote { get; set; } = string.Empty;

    public string? OfferStartOn { get; set; }

    public long? OfferFeeCents { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Die drei Tabellen, die dieser Dienst besitzt — und seine Outbox.</summary>
/// <remarks>
/// Es gibt keine gemeinsame Datenbank und deshalb auch keine gemeinsame Outbox
/// (ADR-0004/0025) — sie hängt in <em>diesen</em> Kontext, damit die Absicht mit
/// der Änderung committet, die sie ausgelöst hat.
/// </remarks>
public sealed class TransferDbContext(DbContextOptions<TransferDbContext> options)
    : DbContext(options)
{
    /// <summary>Die Marktstatus.</summary>
    public DbSet<MarktZeile> Marktstatus => Set<MarktZeile>();

    /// <summary>Die Anfragen.</summary>
    public DbSet<AnfrageZeile> Anfragen => Set<AnfrageZeile>();

    /// <summary>Die Vorgänge.</summary>
    public DbSet<VorgangsZeile> Vorgaenge => Set<VorgangsZeile>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<MarktZeile>(entity =>
        {
            entity.ToTable("market_status");
            // KEINE zweite Spalte fuer die Person. Der Schluessel IST sie, und
            // `market_requests` und `transfers` fuehren daneben eine eigene
            // `subject_id` — wer die beiden vergleicht, haelt das hier fuer
            // vergessen und traegt die Spalte nach. Dann stuenden zwei Angaben
            // ueber denselben Menschen nebeneinander, und die Loeschung (ADR-0027)
            // traefe die eine und liesse die andere stehen. Die Anmerkung sagt es
            // stattdessen aus: sie ist das, was `LoeschempfaengerTests` liest, wo
            // kein Spaltenname zu finden ist.
            entity.HasAnnotation(Personenzeile.Anmerkung, true);
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.Availability)
                .HasColumnName("availability").HasMaxLength(16).IsRequired();
            entity.Property(zeile => zeile.Employed).HasColumnName("employed").IsRequired();
            entity.Property(zeile => zeile.Note)
                .HasColumnName("note").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<AnfrageZeile>(entity =>
        {
            entity.ToTable("market_requests");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(zeile => zeile.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(zeile => zeile.RequestedBy).HasColumnName("requested_by");
            entity.Property(zeile => zeile.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.AnsweredAt).HasColumnName("answered_at");

            entity.HasIndex(zeile => zeile.SubjectId);
            entity.HasIndex(zeile => zeile.TenantId);

            // Einmal fragen — in der Datenbank und nicht nur im Handler, denn
            // zwei gleichzeitige Anfragen kämen beide durch eine Prüfung im
            // Code, und eine Ablehnung wäre wirkungslos.
            entity.HasIndex(zeile => new { zeile.SubjectId, zeile.TenantId })
                .IsUnique()
                .HasDatabaseName("uq_market_requests_subject_tenant");
        });

        modelBuilder.Entity<VorgangsZeile>(entity =>
        {
            entity.ToTable("transfers");
            entity.HasKey(zeile => zeile.Id);
            entity.Property(zeile => zeile.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(zeile => zeile.SubjectId).HasColumnName("subject_id").IsRequired();
            entity.Property(zeile => zeile.TenantId).HasColumnName("tenant_id").IsRequired();
            entity.Property(zeile => zeile.Status)
                .HasColumnName("status").HasMaxLength(16).IsRequired();
            entity.Property(zeile => zeile.RequiresRelease)
                .HasColumnName("requires_release").IsRequired();
            entity.Property(zeile => zeile.ReleaseConfirmed)
                .HasColumnName("release_confirmed").IsRequired();
            entity.Property(zeile => zeile.Message)
                .HasColumnName("message").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.OfferNote)
                .HasColumnName("offer_note").HasColumnType("text").IsRequired();
            entity.Property(zeile => zeile.OfferStartOn)
                .HasColumnName("offer_start_on").HasMaxLength(7);
            entity.Property(zeile => zeile.OfferFeeCents)
                .HasColumnName("offer_fee_cents").HasColumnType("bigint");
            entity.Property(zeile => zeile.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(zeile => zeile.UpdatedAt).HasColumnName("updated_at").IsRequired();

            entity.HasIndex(zeile => zeile.SubjectId);
            entity.HasIndex(zeile => zeile.TenantId);

            // Genau EIN laufender Vorgang je (Person, Unternehmen). Ein zweiter
            // wäre Nachfassen an der Absage vorbei. Teilindex, weil
            // abgeschlossene Vorgänge beliebig oft nebeneinander stehen dürfen.
            entity.HasIndex(zeile => new { zeile.SubjectId, zeile.TenantId })
                .IsUnique()
                .HasDatabaseName("uq_running_transfer")
                .HasFilter(Laufendfilter());
        });

        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Der Teilindex-Filter, aus derselben Menge wie das Aggregat.</summary>
    /// <remarks>
    /// Aus <see cref="Transferstaende.Laufende"/> gebaut statt hier
    /// abgeschrieben: zwei Listen, die dasselbe meinen, driften auseinander —
    /// und die Datenbank wäre die, die es nicht sagt.
    /// </remarks>
    private static string Laufendfilter() =>
        "status IN ("
        + string.Join(", ", Transferstaende.Laufende.Select(
            stand => $"'{Transferstaende.Wort(stand)}'"))
        + ")";
}
