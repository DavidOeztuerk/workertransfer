using System.Text.Json.Serialization;

namespace WorkerTransfer.Contracts.Identity;

/// <summary>
/// Welche Rolle jemand in einem Unternehmen hat — Antwort für
/// <c>GET /internal/companies/{tenantId}/members/{subjectId}/role</c>.
/// </summary>
/// <remarks>
/// <para><strong>Ein TYPISIERTER Vertrag, kein anonymes Objekt.</strong> Vier
/// Benachrichtigungsdrähte kamen nie an, weil an ihrer Stelle ein
/// <c>new { … }</c> stand und der Empfänger eine andere Eigenschaft erwartete.
/// Ein Draht, an dem eine Zugriffsentscheidung hängt, darf das nicht können:
/// ein nicht gelesenes Feld wäre hier stillschweigend „kein Admin".</para>
///
/// <para><strong>Eine Frage, eine Antwort.</strong> Die Alternative wäre, die
/// bestehende Mitgliederliste zu erweitern — dann reiste für jede
/// Rechteprüfung die ganze Belegschaft über den Draht, nur um eine Zeile
/// daraus zu lesen. Hier geht genau das hinaus, was gefragt wurde.</para>
///
/// <para><c>null</c> heißt „gehört nicht dazu" und ist von „gibt es nicht"
/// nicht zu unterscheiden — derselbe Grund wie überall sonst: der Unterschied
/// wäre eine Auskunft darüber, welche Unternehmen es gibt.</para>
/// </remarks>
public sealed record FirmenrolleV1(
    [property: JsonPropertyName("role")] string? Rolle);
