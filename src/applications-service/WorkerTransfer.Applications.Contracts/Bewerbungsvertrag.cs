using System.Text.Json.Serialization;

namespace WorkerTransfer.Applications.Contracts;

/// <summary>Eine Bewerbung, wie sie gelesen wird.</summary>
/// <remarks>
/// Trägt <em>keine</em> Profildaten — nur eine <c>subject_id</c>. Wer Profil,
/// Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste, und
/// dort greift der Consent-Ledger. Ein zweiter Weg an dieselben Daten hätte
/// einen zweiten Filter, und der weicht irgendwann vom ersten ab.
/// <para>
/// Und keinen Punktwert, keinen Rang, keine Reihung. Eine Bewerberliste, die
/// nach etwas sortiert wäre, das dieser Dienst gerechnet hat, ist die
/// Kandidatenbewertung durch die Hintertür (ADR-0022).
/// </para>
/// </remarks>
public sealed record BewerbungV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("shares_resume")] bool SharesResume,
    [property: JsonPropertyName("shares_portfolio")] bool SharesPortfolio,
    /// <summary>Welche Unterlagen beilagen — die Kennungen, in ihrer Reihenfolge.</summary>
    /// <remarks>
    /// Eine Momentaufnahme: was tatsächlich hinausging, nicht „alles, was die
    /// Person hat". Der Inhalt kommt über resume-service und wird dort gegen
    /// den Ledger geprüft — hier stehen nur die Kennungen (ADR-0035).
    /// </remarks>
    [property: JsonPropertyName("documents")] IReadOnlyList<Guid> Documents,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Was eine Person schickt, um sich zu bewerben.</summary>
/// <remarks>
/// Ohne <c>subject_id</c> und ohne <c>tenant_id</c>: wer sich bewirbt, steht im
/// geprüften Token, und zu wem die Stelle gehört, sagt der Jobs-Dienst. Beides
/// im Rumpf wäre ein Weg, sich im Namen eines anderen zu bewerben.
/// <para>
/// <c>shares_resume</c> und <c>shares_portfolio</c> sind wählbar,
/// „Profil teilen" ist es nicht — eine Bewerbung ohne jede Angabe zur Person
/// ist keine.
/// </para>
/// </remarks>
public sealed record BewerbungAbschickenV1(
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("message")] string Message = "",
    [property: JsonPropertyName("shares_resume")] bool SharesResume = false,
    [property: JsonPropertyName("shares_portfolio")] bool SharesPortfolio = false);

/// <summary>Wohin das Unternehmen die Bewerbung bewegt.</summary>
public sealed record BewerbungBewegenV1(
    [property: JsonPropertyName("status")] string Status);

/// <summary>Zahlen über die <em>eigenen</em> Vorgänge.</summary>
/// <remarks>
/// Nichts hier verrechnet Bewerbungen mit Marktstatus, Lebenslauf oder
/// Vorgängen bei anderen Firmen. Die Grenze verläuft nicht bei der Aggregation,
/// sondern bei der Zusammenführung (ADR-0022/0026).
/// </remarks>
public sealed record BewerbungszahlenV1(
    [property: JsonPropertyName("by_status")] IReadOnlyDictionary<string, int> ByStatus,
    [property: JsonPropertyName("total")] int Total);

/// <summary>Eine Anmerkung am Entwurf.</summary>
/// <remarks>
/// <c>quote</c> ist die markierte Stelle. Sie reist mit, damit der nächste
/// Leser weiß, worauf sich die Anmerkung bezog — auch wenn der Text sich
/// inzwischen geändert hat.
/// </remarks>
public sealed record AnmerkungV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("resolved")] bool Resolved,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>Ein Anschreiben auf dem Weg zur Bewerbung.</summary>
/// <remarks>
/// Kein Punktwert, keine Note, keine Reihung: was hier steht, ist der Text der
/// Person und der Stand seines Weges (ADR-0034).
/// </remarks>
public sealed record EntwurfV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("shares_resume")] bool SharesResume,
    [property: JsonPropertyName("documents")] IReadOnlyList<Guid> Documents,
    [property: JsonPropertyName("comments")] IReadOnlyList<AnmerkungV1> Comments,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Für welche Stellen Entwürfe entstehen sollen.</summary>
public sealed record EntwuerfeAnlegenV1(
    [property: JsonPropertyName("job_ids")] IReadOnlyList<Guid> JobIds);

/// <summary>Eigene Änderungen am Text.</summary>
public sealed record EntwurfAendernV1(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("body")] string? Body);

/// <summary>Was mitgeht.</summary>
public sealed record BeilagenV1(
    [property: JsonPropertyName("shares_resume")] bool SharesResume,
    [property: JsonPropertyName("documents")] IReadOnlyList<Guid>? Documents);

/// <summary>Eine Anmerkung schreiben.</summary>
public sealed record AnmerkenV1(
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("quote")] string? Quote);
