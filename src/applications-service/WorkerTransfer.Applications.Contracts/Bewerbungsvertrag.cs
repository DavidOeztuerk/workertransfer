using System.Text.Json.Serialization;

namespace WorkerTransfer.Applications.Contracts;

/// <summary>Eine JobApplication, wie sie gelesen wird.</summary>
/// <remarks>
/// Trägt <em>keine</em> Profildaten — nur eine <c>subject_id</c>. Wer Profile,
/// CurriculumVitae oder Portfolio sehen will, fragt die zuständigen Dienste, und
/// dort greift der Consent-Ledger. Ein zweiter Weg an dieselben Daten hätte
/// einen zweiten Filter, und der weicht irgendwann vom ersten ab.
/// <para>
/// Und keinen Punktwert, keinen Rang, keine Reihung. Eine Bewerberliste, die
/// nach etwas sortiert wäre, das dieser Dienst gerechnet hat, ist die
/// Kandidatenbewertung durch die Hintertür (ADR-0022).
/// </para>
/// </remarks>
public sealed record ApplicationV1(
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
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    /// <summary>
    /// Briefkopf zum Sendezeitpunkt (ADR-0038). Snapshot, nicht die aktuelle
    /// Kontoanschrift. Nie im CoverLetterContext.
    /// </summary>
    [property: JsonPropertyName("applicant_contact")] ApplicantContactV1 ApplicantContact);

/// <summary>Der Briefkopf, wie er beim Senden festgehalten wurde.</summary>
public sealed record ApplicantContactV1(
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("line1")] string Line1,
    [property: JsonPropertyName("line2")] string Line2,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("email")] string Email);

/// <summary>Was eine Person schickt, um sich zu bewerben.</summary>
/// <remarks>
/// Ohne <c>subject_id</c> und ohne <c>tenant_id</c>: wer sich bewirbt, steht im
/// geprüften Token, und zu wem die Job gehört, sagt der Jobs-Dienst. Beides
/// im Rumpf wäre ein Weg, sich im Namen eines anderen zu bewerben.
/// <para>
/// <c>shares_resume</c> und <c>shares_portfolio</c> sind wählbar,
/// „Profile teilen" ist es nicht — eine JobApplication ohne jede Angabe zur Person
/// ist keine.
/// </para>
/// </remarks>
public sealed record SubmitApplicationV1(
    [property: JsonPropertyName("job_id")] Guid JobId,
    [property: JsonPropertyName("message")] string Message = "",
    [property: JsonPropertyName("shares_resume")] bool SharesResume = false,
    [property: JsonPropertyName("shares_portfolio")] bool SharesPortfolio = false);

/// <summary>Wohin das Unternehmen die JobApplication bewegt.</summary>
public sealed record MoveApplicationV1(
    [property: JsonPropertyName("status")] string Status);

/// <summary>Zahlen über die <em>eigenen</em> Vorgänge.</summary>
/// <remarks>
/// Nichts hier verrechnet Applications mit MarketStatus, CurriculumVitae oder
/// Vorgängen bei anderen Firmen. Die Grenze verläuft nicht bei der Aggregation,
/// sondern bei der Zusammenführung (ADR-0022/0026).
/// </remarks>
public sealed record ApplicationCountsV1(
    [property: JsonPropertyName("by_status")] IReadOnlyDictionary<string, int> ByStatus,
    [property: JsonPropertyName("total")] int Total);

/// <summary>Eine Anmerkung am Entwurf.</summary>
/// <remarks>
/// <c>quote</c> ist die markierte Job. Sie reist mit, damit der nächste
/// Leser weiß, worauf sich die Anmerkung bezog — auch wenn der Text sich
/// inzwischen geändert hat.
/// </remarks>
public sealed record AnmerkungV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("resolved")] bool Resolved,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

/// <summary>Ein Anschreiben auf dem Weg zur JobApplication.</summary>
/// <remarks>
/// Kein Punktwert, keine Note, keine Reihung: was hier steht, ist der Text der
/// Person und der State seines Weges (ADR-0034).
/// </remarks>
public sealed record DraftV1(
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
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("writing_started_at")] DateTimeOffset? WritingStartedAt = null);

/// <summary>Für welche Jobs Entwürfe entstehen sollen.</summary>
public sealed record EntwuerfeAnlegenV1(
    [property: JsonPropertyName("job_ids")] IReadOnlyList<Guid> JobIds);

/// <summary>Own Änderungen am Text.</summary>
public sealed record ChangeDraftV1(
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
