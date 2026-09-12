using System.Text.Json.Serialization;

namespace WorkerTransfer.Resume.Contracts;

/// <summary>One position on the wire.</summary>
/// <remarks>
/// The months are strings in the form <c>YYYY-MM</c>, not dates. A date type
/// would force a day onto every entry, and the domain refuses to hold one —
/// no résumé names the 14th of March.
/// <para>
/// The property names are snake_case and stay that way. The browser app was
/// written against the Python service, and a boundary that renames its fields
/// in a migration breaks every caller for no gain (see identity-service, which
/// does the same).
/// </para>
/// </remarks>
public sealed record StationV1(
    [property: JsonPropertyName("employer")] string Employer,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("description")] string Description = "",
    /// <summary>Womit dort gearbeitet wurde — von der Person selbst genannt.</summary>
    /// <remarks>
    /// Vereinheitlicht durch denselben Wortschatz wie das Profil, damit
    /// „postgres" hier wie dort „PostgreSQL" heisst (ADR-0023). Sie machen die
    /// Station nicht durchsuchbar: ein Lebenslauf ist einzeln freigegeben
    /// (ADR-0020), und suchbar wird eine Fähigkeit erst im Profil.
    /// </remarks>
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>One stretch of education on the wire.</summary>
/// <param name="Kind">
/// <c>schule</c> oder <c>ausbildung</c>. Fehlt es, ist es eine berufliche
/// Ausbildung — ältere Zeilen tragen das Feld nicht.
/// </param>
public sealed record AusbildungV1(
    [property: JsonPropertyName("institution")] string Institution,
    [property: JsonPropertyName("qualification")] string Qualification,
    [property: JsonPropertyName("started_on")] string StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("kind")] string? Kind = null);

/// <summary>A résumé as it is read.</summary>
/// <remarks>
/// Carries no visibility field of any kind, and must not grow one: whether it
/// may be shown lives only in the consent ledger (ADR-0020). A flag here would
/// be a second truth, and the two would disagree the first time somebody
/// withdrew.
/// </remarks>
public sealed record LebenslaufV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("positions")] IReadOnlyList<StationV1> Positions,
    [property: JsonPropertyName("education")] IReadOnlyList<AusbildungV1> Education,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    /// <summary>In welcher Vorlage der Lebenslauf gesetzt wird.</summary>
    /// <remarks>
    /// Ein NAME, kein Layout: wie es aussieht, liegt in der Oberfläche und
    /// gilt für beide Seiten — die Person sieht dasselbe wie das Unternehmen,
    /// dem sie den Lebenslauf schickt (ADR-0035).
    /// </remarks>
    [property: JsonPropertyName("template")] string Template = "schlicht");

/// <summary>Eine beigelegte Unterlage — ohne ihre Bytes.</summary>
/// <remarks>
/// Der Inhalt kommt über eine eigene Adresse, damit eine Liste nicht ein
/// Dutzend Dateien mitschleppt. <c>content_type</c> ist das, was die
/// SIGNATUR ergeben hat, nie das, was der Hochladende behauptet hat.
/// </remarks>
public sealed record UnterlageV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("size_bytes")] int SizeBytes,
    [property: JsonPropertyName("uploaded_at")] DateTimeOffset UploadedAt);

/// <summary>Was in einer Unterlage gelesen wurde — oder dass noch nie gelesen wurde.</summary>
/// <param name="ReadAt">
/// <c>null</c> heisst <strong>noch nie gelesen</strong>. Der Zustand ist nicht
/// dasselbe wie „gelesen und nichts gefunden", und die beiden dürfen nie
/// zusammenfallen: sonst sagte die Oberfläche jemandem stillschweigend, in
/// seinem Meisterbrief stehe nichts (ADR-0022 §3).
/// </param>
/// <param name="HasText">
/// Ob überhaupt Text zu lesen war. Ein abfotografierter Gesellenbrief ist ein
/// Bild — darin steht nichts, was ohne Texterkennung auf Bildern zu finden
/// wäre, und die Antwort sagt das, statt zu schweigen.
/// </param>
/// <param name="Terms">
/// Die kanonischen Namen, die der Wortschatz im Text wiedererkannt hat. Ein
/// <strong>Beleg</strong> und keine Nennung: eine Aussage über dieses Dokument,
/// nirgends durchsuchbar, und erst ein Klick und ein Speichern im Profil machen
/// daraus eine Aussage über den Menschen (ADR-0033).
/// <para>
/// <strong>Kein Wortlaut.</strong> Der gefundene Text geht weder hinaus noch in
/// die Datenbank; hinaus gehen allein die Namen.
/// </para>
/// </param>
public sealed record UnterlagenfundV1(
    [property: JsonPropertyName("document_id")] Guid DocumentId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("read_at")] DateTimeOffset? ReadAt,
    [property: JsonPropertyName("has_text")] bool HasText,
    [property: JsonPropertyName("terms")] IReadOnlyList<string> Terms);

/// <summary>Welche Vorlage gewählt wird.</summary>
public sealed record VorlageWaehlenV1(
    [property: JsonPropertyName("template")] string Template);

/// <summary>A résumé as it is written.</summary>
/// <remarks>
/// No subject id. Whose résumé this is comes from the verified token, so a
/// caller cannot write into somebody else's.
/// </remarks>
public sealed record LebenslaufSpeichernV1(
    [property: JsonPropertyName("positions")] IReadOnlyList<StationV1> Positions,
    [property: JsonPropertyName("education")] IReadOnlyList<AusbildungV1> Education);

/// <summary>A request as either side sees it.</summary>
/// <param name="Active">
/// Whether the release holds right now — <c>null</c> where the reader is not
/// entitled to that answer.
/// </param>
/// <remarks>
/// The person sees it filled, because it is the one place where "was granted"
/// and "holds now" visibly come apart. The company sees <c>null</c>: it already
/// has the answer in the form of the data it does or does not get, and a field
/// here could be polled without ever reading a résumé.
/// <para>
/// There is no score, no rank and no percentage on this record, and none
/// belongs on it (ADR-0022).
/// </para>
/// </remarks>
public sealed record AnfrageV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("answered_at")] DateTimeOffset? AnsweredAt,
    [property: JsonPropertyName("active")] bool? Active);
