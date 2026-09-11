using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>Wer ein Unternehmen ist: ein Name und eine bewiesene Domain.</summary>
/// <remarks>
/// <para>Antwort von <c>GET /internal/companies/{tenantId}</c>, hinter dem
/// geteilten Geheimnis und ohne Gateway-Route.</para>
///
/// <para><strong>Die Domain ist der Grund, aus dem es diesen Vertrag gibt.</strong>
/// Eine Person darf Unternehmen ausschliessen und benennt sie durch ihre Domain
/// (ADR-0037). Sie ist keine Auskunft über einen Menschen: sie steht auf jeder
/// Karriereseite, und sie ist aus einer <em>bestätigten</em> Adresse entstanden
/// (ADR-0019), also nicht zu fälschen.</para>
///
/// <para>Was hier <strong>nicht</strong> steht, ist die Belegschaft. Dafür gibt
/// es <see cref="UnternehmensmitgliederAntwortV1"/>, und das ist eine eigene
/// Tür mit einer eigenen Frage.</para>
/// </remarks>
public sealed record UnternehmensauskunftV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("domain")] string Domain);

/// <summary>Wie ein Mensch heisst, und wo man ihm antwortet.</summary>
/// <remarks>
/// <para>Antwort von <c>GET /internal/account/{subjectId}/identity</c>.</para>
///
/// <para><strong>Zwei Felder, und mehr dürfen es nie werden.</strong> Keine
/// Postanschrift und kein Telefon: ADR-0038 legt die Anschrift als
/// <em>Vorlage</em> für einen Briefkopf ab, und sie gehört zu einer Bewerbung,
/// die die Person selbst sendet — nicht in die Ansicht eines Unternehmens, das
/// gerade ein Gespräch führt.</para>
///
/// <para><strong>Das Geheimnis am Endpunkt ist nicht die ganze Erlaubnis.</strong>
/// Es beweist, dass ein Dienst fragt, nicht dass er fragen darf: die Erlaubnis
/// steht im Ledger (<c>advisor.identity:tenant:&lt;id&gt;</c>) und wird
/// <em>vor</em> diesem Aufruf geholt. Wer diese Tür für etwas anderes benutzt,
/// holt sich die Prüfung dazu.</para>
/// </remarks>
public sealed record PersonenauskunftV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email);
