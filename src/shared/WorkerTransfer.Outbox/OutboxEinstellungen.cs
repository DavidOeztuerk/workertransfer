namespace WorkerTransfer.Outbox;

/// <summary>How the dispatcher paces itself.</summary>
public sealed class OutboxEinstellungen
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Abschnitt = "Outbox";

    /// <summary>Which table. Only where a service's conventions differ.</summary>
    public string Tabelle { get; set; } = OutboxModelBuilderExtensions.Tabelle;

    /// <summary>How many rows one pass takes.</summary>
    public int Stapelgroesse { get; set; } = 50;

    /// <summary>
    /// After how many failed attempts a row is left lying, or <c>null</c> for
    /// never giving up.
    /// </summary>
    /// <remarks>
    /// The default is right for a notification: the row does not disappear, it
    /// is only no longer tried. For an <em>erasure</em> exactly that would be
    /// the silent failure ADR-0027 exists against — a promise nobody redeems,
    /// and nobody sees it. There it is <c>null</c>, and a permanently dead
    /// recipient therefore <em>blocks</em> completion. That is the point, and no
    /// timeout may "finish" it.
    /// </remarks>
    public int? HoechsteVersuche { get; set; } = Zustellgrenzen.Standard;

    /// <summary>How long between passes while things are moving.</summary>
    public TimeSpan Takt { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How far the interval may grow while nothing gets through.
    /// </summary>
    /// <remarks>
    /// The counterpart to never giving up: whoever never gives up must not run
    /// into a wall once a second. It grows against a wall, not against quiet —
    /// an empty table delivers nothing but is not a failure.
    /// </remarks>
    public TimeSpan HoechsterTakt { get; set; } = TimeSpan.FromMinutes(5);
}
