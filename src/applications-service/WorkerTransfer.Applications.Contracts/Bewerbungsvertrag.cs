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
