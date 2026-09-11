using System.Text.Json.Serialization;

namespace WorkerTransfer.Advisor.Contracts;

/// <summary>Das Mandat, wie die Person es sieht und schreibt.</summary>
/// <remarks>
/// <para>Vier Werte und ein Zeitstempel — und ausdrücklich <strong>kein</strong>
/// Sichtbarkeitsfeld. Sichtbarkeit lebt im Ledger (ADR-0020); ein Schalter an
/// dieser Stelle wäre eine zweite Wahrheit, und eine, die der Client
/// mitschicken könnte.</para>
///
/// <para>Alle Felder sind <c>null</c>-fähig, weil „nichts gesagt" der Normalfall
/// ist. Ein leeres Mandat ist vollständig.</para>
/// </remarks>
public sealed record MandatV1(
    [property: JsonPropertyName("entry_month")] string? EntryMonth,
    [property: JsonPropertyName("salary_min")] int? SalaryMin,
    [property: JsonPropertyName("salary_max")] int? SalaryMax,
    [property: JsonPropertyName("workload_percent")] int? WorkloadPercent,
    [property: JsonPropertyName("excluded_domains")] IReadOnlyList<string> ExcludedDomains,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Was die Person schickt, um ihr Mandat zu schreiben.</summary>
public sealed record MandatSchreibenV1(
    [property: JsonPropertyName("entry_month")] string? EntryMonth = null,
    [property: JsonPropertyName("salary_min")] int? SalaryMin = null,
    [property: JsonPropertyName("salary_max")] int? SalaryMax = null,
    [property: JsonPropertyName("workload_percent")] int? WorkloadPercent = null,
    [property: JsonPropertyName("excluded_domains")] IReadOnlyList<string>? ExcludedDomains = null);

/// <summary>Was ein Unternehmen schickt, um ein Gespräch zu eröffnen.</summary>
public sealed record GespraechEroeffnenV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("note")] string? Note = null);

/// <summary>Welche Stufe gemeint ist.</summary>
public sealed record StufeV1([property: JsonPropertyName("stage")] int? Stage = null);

/// <summary>Ein Gespräch, wie die PERSON es sieht.</summary>
/// <remarks>
/// Sie sieht alles: welches Unternehmen, wo es steht, wie weit sie geöffnet
/// hat. Über sie selbst erfährt sie hier nichts Neues — es ist ihr eigenes
/// Mandat.
/// </remarks>
public sealed record MeinGespraechV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("stage")] int Stage,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("opened_at")] DateTimeOffset OpenedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>
/// Ein Gespräch, wie das UNTERNEHMEN es sieht — und der wichtigste Typ dieses
/// Dienstes.
/// </summary>
/// <remarks>
/// <para><strong>Was in einer Stufe nicht freigegeben ist, existiert hier
/// nicht.</strong> Nicht als <c>null</c>, nicht als „gesperrt", nicht als leere
/// Zeichenkette: das Feld <em>fehlt</em> im JSON. Deshalb steht an dieser Klasse
/// <see cref="JsonIgnoreAttribute"/> mit
/// <see cref="JsonIgnoreCondition.WhenWritingNull"/> an jedem stufenabhängigen
/// Feld.</para>
///
/// <para>Der Unterschied ist nicht kosmetisch. Ein <c>"salary_min": null</c>
/// sagt: es gibt ein Feld für ein Gehalt, du siehst es nur nicht — und damit,
/// dass es etwas zu sehen gäbe. Ein fehlendes Feld sagt nichts. Und es sagt
/// <em>dasselbe</em> nichts, wenn die Person die Stufe freigegeben, aber nie
/// ein Gehalt genannt hat: verborgen und nicht vorhanden sind ununterscheidbar
/// (ADR-0020 §1, ADR-0037).</para>
///
/// <para><c>stage</c> selbst steht immer da, und das ist kein Widerspruch: wie
/// weit ein Gespräch geöffnet ist, ist eine Aussage über das <em>Gespräch</em>
/// und nicht über verborgene Inhalte. Ohne sie wüsste ein Unternehmen nicht, ob
/// es noch fragen soll — und ein Dienst, der das verschweigt, erzeugt genau die
/// Nachfragen, die er verhindern will.</para>
/// </remarks>
public sealed record GespraechV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("stage")] int Stage,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("opened_at")] DateTimeOffset OpenedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
{
    /// <summary>Ab Stufe 1: der genannte Eintrittsmonat.</summary>
    [JsonPropertyName("entry_month")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntryMonth { get; init; }

    /// <summary>Ab Stufe 1: das genannte Pensum.</summary>
    [JsonPropertyName("workload_percent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WorkloadPercent { get; init; }

    /// <summary>Ab Stufe 2: die untere Grenze der Gehaltsspanne.</summary>
    [JsonPropertyName("salary_min")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SalaryMin { get; init; }

    /// <summary>Ab Stufe 2: die obere.</summary>
    [JsonPropertyName("salary_max")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SalaryMax { get; init; }

    /// <summary>Ab Stufe 3: der bürgerliche Name.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>Ab Stufe 3: die Adresse, unter der man antworten kann.</summary>
    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }
}

/// <summary>Eine Liste von Gesprächen.</summary>
/// <remarks>
/// Keine Gesamtzahl (ADR-0026): sie verriete über die Differenz zur Länge, wie
/// viele Gespräche gerade <em>nicht</em> auf Stufe 1 stehen — und damit, wie
/// viele Menschen zurückgezogen haben.
/// </remarks>
public sealed record GespraechslisteV1(
    [property: JsonPropertyName("items")] IReadOnlyList<GespraechV1> Items);

/// <summary>Dasselbe für die Person.</summary>
public sealed record MeineGespraechslisteV1(
    [property: JsonPropertyName("items")] IReadOnlyList<MeinGespraechV1> Items);

/// <summary>Was an notification-service geht: über wen, und welcher Art.</summary>
/// <remarks>
/// <para>Ein <em>typisierter</em> Vertrag und kein anonymes Objekt.
/// <c>new { userId = … }</c> kompiliert, serialisiert, und kommt am Empfänger,
/// der <c>[JsonPropertyName("user_id")]</c> deklariert, als <c>Guid.Empty</c>
/// an — gemessen an vier Sprüngen, die deshalb nie angekommen sind.</para>
///
/// <para>Zwei Felder, und mehr dürfen es nie werden. Kein Firmenname, kein
/// Text: eine Mail landet in einem Postfach, und dieses Postfach kann das des
/// jetzigen Arbeitgebers sein.</para>
/// </remarks>
public sealed record GespraechsmeldungV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);
