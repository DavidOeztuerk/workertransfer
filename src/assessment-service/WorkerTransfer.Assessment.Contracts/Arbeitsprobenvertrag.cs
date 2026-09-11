using System.Text.Json.Serialization;

namespace WorkerTransfer.Assessment.Contracts;

/// <summary>Was ein Unternehmen schickt, um eine Aufgabe zu stellen.</summary>
/// <remarks>
/// <para><c>hours</c> und <c>due_at</c> sind <strong>Pflicht</strong>, und das
/// ist der ganze Punkt dieses Dienstes (ADR-0042 §3). Ein optionaler Umfang
/// wäre ein Umfang, den man weglässt — und eine Arbeitsprobe ohne genannten
/// Umfang ist eine Aufgabe, deren Preis die Person erst kennt, wenn sie ihn
/// bezahlt hat.</para>
///
/// <para>Deshalb steht hier auch kein Vorgabewert: <c>hours = 4</c> wäre eine
/// Zahl, die der Server sich ausdenkt und die Person für eine Angabe des
/// Unternehmens hält.</para>
/// </remarks>
public sealed record AufgabeStellenV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("task")] string? Task,
    [property: JsonPropertyName("hours")] int? Hours,
    [property: JsonPropertyName("due_at")] DateTimeOffset? DueAt);

/// <summary>Was die Person schickt, um einzureichen.</summary>
/// <remarks>
/// Text, Adresse, oder beides — aber nicht nichts. Und keine Datei: dieser
/// Dienst nimmt keine Bytes entgegen, weil eine dritte Ablage ein dritter Ort
/// wäre, den die Löschung erreichen muss.
/// </remarks>
public sealed record EinreichenV1(
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("url")] string? Url = null);

/// <summary>Was ein Unternehmen schickt, um zu bewerten.</summary>
/// <remarks>
/// <strong>Beide Felder sind Pflicht, auch bei <c>rejected</c>.</strong>
/// Ablehnen und Begründen sind ein Schritt und nicht zwei — ein leerer Text ist
/// 422 (ADR-0042 §2).
/// </remarks>
public sealed record BewertenV1(
    [property: JsonPropertyName("outcome")] string? Outcome = null,
    [property: JsonPropertyName("text")] string? Text = null);

/// <summary>Die abgegebene Lösung.</summary>
public sealed record EinreichungV1(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("submitted_at")] DateTimeOffset SubmittedAt);

/// <summary>Die Rückmeldung — Text und ein Ausgang, nie eine Zahl.</summary>
/// <remarks>
/// <para>Kein <c>score</c>, kein <c>rating</c>, keine Sterne, kein „3 von 5".
/// Eine Zahl verbirgt genau das, was hilft — was am Ergebnis fehlte — und sieht
/// dabei aus wie eine Messung (ADR-0022, ADR-0042 §1).</para>
///
/// <para><c>outcome</c> ist <c>accepted</c> oder <c>rejected</c> und handelt
/// vom <em>Vorgang</em>: „wir machen weiter" oder „wir machen nicht weiter". Es
/// gibt keine Stelle, an der solche Ausgänge über Vorgänge hinweg gezählt
/// werden.</para>
/// </remarks>
public sealed record BewertungV1(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("evaluated_at")] DateTimeOffset EvaluatedAt);

/// <summary>
/// Ein Vorgang — und der einzige Typ, den dieser Dienst über ihn herausgibt.
/// </summary>
/// <remarks>
/// <para><strong>Beide Seiten bekommen dieses Dokument, byte-gleich.</strong>
/// Es gibt keine Firmensicht neben einer Personensicht, und das ist der
/// Mechanismus hinter „die Person sieht die Bewertung, immer" (ADR-0042 §2):
/// zwei Sichten wären die Stelle, an der die beiden auseinanderlaufen — und
/// zwar erst Monate später, beim nächsten Feld. Ein Test vergleicht die zwei
/// Antworten Byte für Byte.</para>
///
/// <para><c>submission</c> und <c>evaluation</c> fehlen, solange es sie nicht
/// gibt, statt <c>null</c> zu sein. Hier ausdrücklich <em>nicht</em>, um etwas
/// zu verbergen — beide Seiten sehen dasselbe —, sondern weil ein Feld, das nie
/// einen Wert hat, kein Feld ist.</para>
/// </remarks>
public sealed record VorgangV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("hours")] int Hours,
    [property: JsonPropertyName("due_at")] DateTimeOffset DueAt,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt)
{
    /// <summary>Die Lösung, sobald sie da ist.</summary>
    [JsonPropertyName("submission")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EinreichungV1? Submission { get; init; }

    /// <summary>Die Rückmeldung, sobald sie da ist — für beide Seiten.</summary>
    [JsonPropertyName("evaluation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BewertungV1? Evaluation { get; init; }
}

/// <summary>Eine Liste von Vorgängen.</summary>
/// <remarks>
/// Keine Gesamtzahl (ADR-0026): sie zählte über Vorgänge hinweg, und das ist
/// genau der Schritt, mit dem aus einer Rückmeldung zu einem Vorgang eine
/// Aussage über einen Menschen wird.
/// </remarks>
public sealed record VorgangslisteV1(
    [property: JsonPropertyName("items")] IReadOnlyList<VorgangV1> Items);

/// <summary>Was an notification-service geht: über wen, und welcher Art.</summary>
/// <remarks>
/// <para>Ein <em>typisierter</em> Vertrag und kein anonymes Objekt.
/// <c>new { userId = … }</c> kompiliert, serialisiert, und kommt am Empfänger,
/// der <c>[JsonPropertyName("user_id")]</c> deklariert, als <c>Guid.Empty</c>
/// an — gemessen an vier Sprüngen, die deshalb nie angekommen sind.</para>
///
/// <para>Zwei Felder, und mehr dürfen es nie werden. Kein Firmenname, kein
/// Aufgabentext, keine Bewertung: eine Mail landet in einem Postfach, und
/// dieses Postfach kann das des jetzigen Arbeitgebers sein.</para>
/// </remarks>
public sealed record ArbeitsprobenmeldungV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);
