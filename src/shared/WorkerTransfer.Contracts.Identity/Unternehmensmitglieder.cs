using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>Ein Mitglied eines Unternehmens, wie es über den internen Draht reist.</summary>
/// <remarks>
/// Dies ist ein TYPISIERTER VERTRAG. Er wird Dienst-zu-Dienst übergeben, nie
/// als anonymes Objekt. Der Benachrichtigungsdraht kam viermal nie an, genau
/// weil an seiner Stelle ein <c>new { ... }</c> stand — und der Serialisierer
/// beim Empfänger eine andere Eigenschaft erwartete. Ein Vertrag, der hier
/// liegt, wird von beiden Seiten KOMPILIERT geprüft.
/// <para>
/// Nur die Kennung. Ein Anzeigename würde mitreisen und in jeder Sicherung
/// landen, und die Outbox braucht ihn nicht: sie trägt Kennung und Art
/// (ADR-0025), sonst nichts.
/// </para>
/// </remarks>
public sealed record UnternehmensmitgliedV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId);

/// <summary>Antwort für GET /internal/companies/{tenantId}/members.</summary>
public sealed record UnternehmensmitgliederAntwortV1(
    [property: JsonPropertyName("mitglieder")] IReadOnlyList<UnternehmensmitgliedV1> Mitglieder);
