using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Profile.Domain.Audit;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>A row of <c>profiles</c>, as it stands in the database.</summary>
/// <remarks>
/// Not the aggregate. <see cref="EfProfileStore"/> builds a
/// <see cref="PersonProfile"/> from it, so that nothing that happens to the
/// aggregate lands in the table by itself.
/// <para>
/// <b>No column for visibility</b> (ADR-0020). Whoever adds one here has added
/// a second truth — and the one somebody forgets to change along.
/// </para>
/// </remarks>
public sealed class ProfileRow
{
    /// <summary>At the same time the person's id: one profile per human being.</summary>
    public Guid Id { get; set; }

    /// <summary>The column <c>headline</c>.</summary>
    public string Headline { get; set; } = string.Empty;

    /// <summary>The column <c>bio</c>.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The column <c>location</c>.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>The column <c>remote_ok</c>.</summary>
    public bool RemoteOk { get; set; }

    /// <summary>
    /// The column <c>skills</c> as <c>text[]</c>.
    /// </summary>
    /// <remarks>
    /// A real field and no JSON document: Postgres can search and index over it
    /// without anybody keeping a second, lower-cased copy of the same words.
    /// </remarks>
    public string[] Skills { get; set; } = [];

    /// <summary>
    /// The column <c>commute_km</c> — a stage, <c>null</c> for "said nothing".
    /// </summary>
    /// <remarks>
    /// <strong>As text and not as a number</strong>, although the name suggests
    /// a number. A number in the column would be the invitation to compare it
    /// ("WHERE commute_km >= 37") and some day to sort by it — exactly what
    /// ADR-0041 does not want. The stage is a statement, not a measurement.
    /// </remarks>
    public CommuteReadiness? Commute { get; set; }

    /// <summary>The column <c>relocation</c>, <c>null</c> for "said nothing".</summary>
    public RelocationReadiness? Relocation { get; set; }

    /// <summary>The column <c>xing_url</c>, <c>null</c> for none.</summary>
    public string? Xing { get; set; }

    /// <summary>The column <c>created_at</c>.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The column <c>updated_at</c>. At the same time half the ordering.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A row of <c>audit_events</c>.</summary>
/// <remarks>
/// The trail is only appended to, so there is no aggregate that would arise
/// from it again. <c>action</c> is the enum <c>audit_action</c>, stored as the
/// label Npgsql makes of it.
/// </remarks>
public sealed class AuditRow
{
    /// <summary>The row's id.</summary>
    public Guid Id { get; set; }

    /// <summary>Who acted, or <c>null</c> for the erasure cascade.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>For which company, or <c>null</c>.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>What happened.</summary>
    public AuditAction Action { get; set; }

    /// <summary>On whom.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>The correlation id, across service boundaries.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>When.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Technical extras as JSON, only from the list.</summary>
    public string Metadata { get; set; } = "{}";
}

/// <summary>This service's tables — and they belong to it alone.</summary>
/// <remarks>
/// No <c>ExcludeFromMigrations</c> anywhere: unlike in identity, no foreign tool
/// chain creates the tables here. The schema arises from these migrations, and
/// the test container gets it through <c>Database.MigrateAsync()</c> — the same
/// source as operation.
/// <para>
/// Tracking is off for the whole context. Per query it would be discipline,
/// and the next query somebody adds forgets it — <see cref="EfProfileStore"/>
/// sends stored values back through the domain, which normalises them, and a
/// tracked row would afterwards stand there as changed.
/// </para>
/// </remarks>
public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    /// <summary>The profiles.</summary>
    public DbSet<ProfileRow> Profiles => Set<ProfileRow>();

    /// <summary>The audit trail.</summary>
    public DbSet<AuditRow> AuditEntries => Set<AuditRow>();

    /// <summary>The names of this service's Postgres enums.</summary>
    /// <remarks>
    /// As a constant and not as a string at the call site: the name stands in
    /// the database and in the migration, and two spellings of the same name
    /// stand out only at the first insert.
    /// </remarks>
    public static class EnumNames
    {
        /// <summary>Behind <c>audit_events.action</c>.</summary>
        public const string AuditAction = "audit_action";
    }

    /// <summary>
    /// The commute stage as it is STORED — the German member names of old.
    /// </summary>
    /// <remarks>
    /// <para><strong>Not <c>HasConversion&lt;string&gt;()</c>, and that is the
    /// point of these four converters.</strong> That overload stores the C#
    /// member name, and the members were called <c>Bis10</c> … <c>Egal</c>,
    /// <c>Ja</c>/<c>Nein</c>/<c>Offen</c> until the code became English. Renaming
    /// them under the old conversion would have changed every value written
    /// from then on — without a migration, without a build error — and every
    /// existing row would have failed to read.</para>
    ///
    /// <para>So the stored strings are spelt out here and stay what they were.
    /// A new member needs a line in both directions, and the switch throws
    /// rather than guessing at a value it does not know.</para>
    /// </remarks>
    private static readonly ValueConverter<CommuteReadiness, string> CommuteConverter = new(
        value => StoredCommute(value),
        text => ReadCommute(text));

    /// <summary>The relocation choice as it is STORED; see <see cref="CommuteConverter"/>.</summary>
    private static readonly ValueConverter<RelocationReadiness, string> RelocationConverter = new(
        value => StoredRelocation(value),
        text => ReadRelocation(text));

    private static string StoredCommute(CommuteReadiness value) => value switch
    {
        CommuteReadiness.UpTo10 => "Bis10",
        CommuteReadiness.UpTo25 => "Bis25",
        CommuteReadiness.UpTo50 => "Bis50",
        CommuteReadiness.UpTo100 => "Bis100",
        CommuteReadiness.AnyDistance => "Egal",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static CommuteReadiness ReadCommute(string text) => text switch
    {
        "Bis10" => CommuteReadiness.UpTo10,
        "Bis25" => CommuteReadiness.UpTo25,
        "Bis50" => CommuteReadiness.UpTo50,
        "Bis100" => CommuteReadiness.UpTo100,
        "Egal" => CommuteReadiness.AnyDistance,
        _ => throw new InvalidOperationException("unknown stored commute stage")
    };

    private static string StoredRelocation(RelocationReadiness value) => value switch
    {
        RelocationReadiness.Yes => "Ja",
        RelocationReadiness.No => "Nein",
        RelocationReadiness.Open => "Offen",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static RelocationReadiness ReadRelocation(string text) => text switch
    {
        "Ja" => RelocationReadiness.Yes,
        "Nein" => RelocationReadiness.No,
        "Offen" => RelocationReadiness.Open,
        _ => throw new InvalidOperationException("unknown stored relocation choice")
    };

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // `name:` written out, and that is no ornament: the generic overload
        // takes its FIRST argument as the SCHEMA, not as the type name. Written
        // positionally this line created a schema named after the enum and in
        // it a second type — an orphan nobody uses, because the migration had
        // long created the real type in `public`. Measured at the running
        // database (under the enum's former name):
        //
        //   nspname        | typname
        //   public         | pruef_handlung     <- the one in use
        //   pruef_handlung | pruefhandlung      <- the orphan
        //
        // Without consequence in operation, but it arose anew at EVERY
        // migration in EVERY environment. resume-service has always written it
        // named; only this service did not.
        modelBuilder.HasPostgresEnum<AuditAction>(name: EnumNames.AuditAction);

        modelBuilder.Entity<ProfileRow>(row =>
        {
            row.ToTable("profiles");
            row.HasAnnotation(SubjectKeyedTable.Annotation, true);
            row.HasKey(column => column.Id);
            row.Property(column => column.Id).HasColumnName("id");
            row.Property(column => column.Headline).HasColumnName("headline");
            row.Property(column => column.Text).HasColumnName("bio");
            row.Property(column => column.Location).HasColumnName("location");
            row.Property(column => column.RemoteOk).HasColumnName("remote_ok");
            row.Property(column => column.Skills).HasColumnName("skills");
            // As a string and not as a Postgres enum: both are voluntary and
            // nullable, and an enum would need a type migration per extension —
            // for a statement nobody queries, which turns into a tick at search
            // time. The converters keep the stored strings (see above).
            row.Property(column => column.Commute)
                .HasColumnName("commute_km").HasMaxLength(16).HasConversion(CommuteConverter);
            row.Property(column => column.Relocation)
                .HasColumnName("relocation").HasMaxLength(16).HasConversion(RelocationConverter);
            row.Property(column => column.Xing).HasColumnName("xing_url").HasMaxLength(200);
            row.Property(column => column.CreatedAt).HasColumnName("created_at");
            row.Property(column => column.UpdatedAt).HasColumnName("updated_at");

            // Exactly the page's ordering, in exactly its direction. Without it
            // every page reads the whole table — and the ordering is composite,
            // an index on updated_at alone would not help. The index name is
            // schema and stays as the migration created it.
            row.HasIndex(column => new { column.UpdatedAt, column.Id })
                .IsDescending(true, true)
                .HasDatabaseName("ix_profiles_seite");
        });

        modelBuilder.Entity<AuditRow>(row =>
        {
            row.ToTable("audit_events");
            row.HasKey(column => column.Id);
            row.Property(column => column.Id).HasColumnName("id");
            row.Property(column => column.ActorId).HasColumnName("actor_id");
            row.Property(column => column.TenantId).HasColumnName("tenant_id");
            row.Property(column => column.Action).HasColumnName("action");
            row.Property(column => column.TargetId).HasColumnName("target_id");
            row.Property(column => column.CorrelationId).HasColumnName("correlation_id");
            row.Property(column => column.OccurredAt).HasColumnName("occurred_at");
            row.Property(column => column.Metadata).HasColumnName("metadata").HasColumnType("jsonb");

            // Expressly NO foreign key to profiles: the erasure removes the
            // profile row and keeps the trail. A foreign key here would mean
            // either cascade — then the proof that something was deleted would
            // disappear — or prohibition, then the erasure would not go through.
        });

        base.OnModelCreating(modelBuilder);
    }
}
