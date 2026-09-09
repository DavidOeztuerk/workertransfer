using System.Text.Json.Serialization;

namespace WorkerTransfer.Jobs.Contracts;

/// <summary>Eine Anzeige, wie sie gelesen wird.</summary>
/// <remarks>
/// Trägt keinen Punktwert, keinen Rang und keinen Prozentwert — und es gehört
/// keiner darauf. Wie gut jemand zu einer Stelle passt, wird im Browser
/// gerechnet, der <em>Person</em> gezeigt und nie dem Unternehmen (ADR-0022).
/// <para>
/// <c>skills</c> ist die Liste, gegen die der Browser das Profil hält. Deshalb
/// steht sie hier ausgeschrieben und nicht als Zahl: eine Zahl verbürge, welche
/// Anforderung fehlt, und genau die ist das einzig Hilfreiche.
/// </para>
/// </remarks>
public sealed record StelleV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("remote_mode")] string RemoteMode,
    [property: JsonPropertyName("employment_type")] string EmploymentType,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Eine Anzeige, wie sie geschrieben wird.</summary>
/// <remarks>
/// Ohne <c>tenant_id</c>: wem die Anzeige gehört, steht im geprüften Token.
/// Und ohne <c>status</c> — eine neue Anzeige beginnt als Entwurf, und der
/// Übergang ist eine eigene Handlung mit eigener Route.
/// </remarks>
public sealed record StelleSchreibenV1(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("location")] string Location = "",
    [property: JsonPropertyName("postal_code")] string PostalCode = "",
    [property: JsonPropertyName("remote_mode")] string RemoteMode = "none",
    [property: JsonPropertyName("employment_type")] string EmploymentType = "full_time",
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills = null);

// EINE SEITE ANZEIGEN GIBT ES HIER NICHT MEHR — sie ist jetzt
// `Seitenantwort<StelleV1>` aus `ServiceDefaults`, damit jede blätterbare
// Liste denselben Umschlag hat.
//
// Der Satz, der hier stand, verdient es, nicht verlorenzugehen: „ohne
// Gesamtzahl, und aus demselben Grund wie bei den Kandidaten: sie sagte über
// die Differenz etwas darüber aus, was gefiltert wurde."
//
// Das stimmt — bei KANDIDATEN. Dort ist die Differenz zwischen „42 insgesamt"
// und „3 angezeigt" die Auskunft, wie viele Menschen sich verborgen haben, und
// genau die darf niemand bekommen (ADR-0026). Eine Stellenanzeige ist aber
// keine Person: sie wurde von einem Unternehmen veröffentlicht, um gesehen zu
// werden, und „9 Treffer" sagt über niemanden etwas.
//
// Die Kandidatenliste bleibt deshalb beim Zeiger und bekommt KEINE Gesamtzahl.

/// <summary>Was ein Unternehmen schickt, um sich beim Formulieren helfen zu lassen.</summary>
public sealed record EntwurfV1(
    [property: JsonPropertyName("title")] string Title = "",
    [property: JsonPropertyName("description")] string Description = "",
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills = null,
    [property: JsonPropertyName("location")] string Location = "",
    [property: JsonPropertyName("wish")] string Wish = "");
