using System.Text.Json.Serialization;

namespace WorkerTransfer.Notification.Contracts;

/// <summary>Was ein Dienst schickt, um jemanden zu benachrichtigen.</summary>
/// <remarks>
/// Zwei Felder, und mehr darf es nie werden. Kein Text, kein Grund, kein
/// Firmenname: die Art genügt, um in der Anwendung die richtige Stelle zu
/// zeigen, und die Anwendung prüft, wer liest. Ein Feld für einen Satz wäre die
/// Zeile, in der irgendwann „Acme GmbH möchte deinen Marktstatus sehen" steht.
/// </remarks>
public sealed record BenachrichtigenV1(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("kind")] string Kind);

/// <summary>Ein Eintrag im Postfach.</summary>
public sealed record EingangV1(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("read_at")] DateTimeOffset? ReadAt);

/// <summary>Die sechs Schalter.</summary>
/// <remarks>
/// Ohne die Drossel: sie ist keine Einstellung, sondern eine Eigenschaft des
/// Dienstes, und ein Feld dafür wäre eine Einladung, sie abzuschalten.
/// <para>
/// <c>application_received</c> hat den Vorgabewert <c>true</c>: eine neue Art
/// darf nicht stillschweigend ausbleiben, weil ein älterer Rumpf sie nicht
/// kennt. Fehlt das Feld, bleibt der Schalter an.
/// </para>
/// <para>
/// <c>profile_discovered</c> — „dein Profil wurde entdeckt" — steht aus
/// demselben Grund auf <c>true</c>, und es ist ausdrücklich <em>einzeln</em>
/// abbestellbar (ADR-0033): es ist die einzige Auskunft, die eine Person über
/// ihre eigene Sichtbarkeit bekommt, und wer sie nicht will, soll sie einzeln
/// abstellen können, ohne alles andere mit abzustellen.
/// </para>
/// </remarks>
public sealed record BenachrichtigungswuenscheV1(
    [property: JsonPropertyName("resume_request")] bool ResumeRequest,
    [property: JsonPropertyName("market_request")] bool MarketRequest,
    [property: JsonPropertyName("application_update")] bool ApplicationUpdate,
    [property: JsonPropertyName("transfer_update")] bool TransferUpdate,
    [property: JsonPropertyName("application_received")] bool ApplicationReceived = true,
    [property: JsonPropertyName("profile_discovered")] bool ProfileDiscovered = true);
