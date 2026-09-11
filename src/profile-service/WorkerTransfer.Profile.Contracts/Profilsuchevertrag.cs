using System.Text.Json.Serialization;

namespace WorkerTransfer.Profile.Contracts;

/// <summary>Ein Profil, wie die interne Suche es meldet.</summary>
/// <remarks>
/// <para>Nur, was die Person selbst geschrieben hat. Kein Name, keine Adresse,
/// kein Arbeitgeber — der Aufrufer ist ein Dienst, der sucht, und was er nicht
/// bekommt, kann er nicht weiterreichen.</para>
///
/// <para><c>skills</c> ist die Herkunftsklasse <em>genannt</em> aus ADR-0033 und
/// das Einzige, worüber gesucht wird. Belege stehen hier nicht: sie holt
/// scout-service zum Treffer dazu, bei dem Dienst, der sie hält.</para>
///
/// <para><strong>Und kein Sichtbarkeitsfeld.</strong> Ob dieses Profil gezeigt
/// werden darf, steht im Consent-Ledger und nirgends sonst (ADR-0020). Der
/// Aufrufer fragt ihn selbst, für die ganze Seite auf einmal — ein Feld hier
/// wäre eine zweite Wahrheit, und die beiden wären beim ersten Widerruf uneins.</para>
/// </remarks>
public sealed record ProfilfundV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("headline")] string Headline,
    /// <summary>Was die Person über sich geschrieben hat.</summary>
    /// <remarks>
    /// Sie steht hier, weil die Kandidatenkarte sie zeigte, bevor scout-service
    /// sie ablöste. Ein Umzug, bei dem stillschweigend ein Feld verschwindet,
    /// ist kein Umzug, sondern eine Verschlechterung mit anderem Namen.
    /// </remarks>
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    /// <summary>Die Pendelstufe, oder <c>null</c> für „nichts gesagt".</summary>
    /// <remarks>
    /// <strong>Die Stufe reist, nicht eine Kilometerzahl</strong> (ADR-0041).
    /// Was der Empfänger daraus macht, ist ein Häkchen — und <c>null</c> ist
    /// dort der dritte Zustand, nicht ein Nein.
    /// </remarks>
    [property: JsonPropertyName("commute_km")] string? CommuteKm = null,
    [property: JsonPropertyName("relocation")] string? Relocation = null);

/// <summary>Eine Seite der internen Suche.</summary>
/// <remarks>
/// <strong>Ohne Gesamtzahl</strong> (ADR-0026) — auch hier, wo nur ein Dienst
/// mitliest: was es nicht gibt, kann auch nicht versehentlich weitergereicht
/// werden. Die Reihenfolge ist <c>updated_at DESC, id DESC</c>: stabil und
/// sachfremd, damit zwei gleiche Suchen dasselbe liefern.
/// </remarks>
public sealed record ProfilfundseiteV1(
    [property: JsonPropertyName("items")] IReadOnlyList<ProfilfundV1> Items,
    [property: JsonPropertyName("next")] string? Next);
