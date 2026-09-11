using Girder.Core.Identity;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet.</summary>
/// <remarks>
/// Ausdrücklich <em>kein</em> <c>false</c>. „Ich weiss es nicht" ist etwas
/// anderes als „nein", und die beiden gleich zu behandeln hiesse, einen
/// Ausfall in eine Aussage über einen Menschen zu verwandeln. Der Endpunkt
/// macht daraus 503.
/// </remarks>
/// <param name="grund">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class EinwilligungSchweigt(string grund) : Exception(grund);

/// <summary>Die Fähigkeiten, um die es beim Suchen geht.</summary>
/// <remarks>
/// Dieselben Worte wie in profile-service. Eine zweite Schreibweise wäre eine
/// zweite Wahrheit darüber, was jemand freigegeben hat — und die stille
/// Variante davon zeigt Profile, die niemand freigegeben hat.
/// </remarks>
public static class Sichtbarkeiten
{
    /// <summary>„Für alle Unternehmen" — der Schalter auf der Profilseite.</summary>
    public const string Oeffentlich = "profile.visibility:public";

    /// <summary>„Für dieses eine Unternehmen".</summary>
    public static string FuerFirma(TenantId firma) => $"profile.visibility:tenant:{firma.Value}";
}

/// <summary>Darf dieses Unternehmen diesen Menschen sehen?</summary>
/// <remarks>
/// <para>Synchron, je Aufruf, <strong>ohne jeden Zwischenspeicher</strong>
/// (ADR-0013). Ein Widerruf muss beim nächsten Lesen wirken, nicht beim
/// übernächsten — und genau deshalb speichert dieser Dienst kein Ergebnis,
/// sondern nur die Anfrage (ADR-0036 Entscheidung 4).</para>
///
/// <para>Eine Sammelfrage für die ganze Seite (ADR-0030): eine Frage je Zeile
/// wären bis zu hundert Verbindungsaufbauten für eine Ansicht. Die Antworten
/// kommen in der Reihenfolge der Fragen, und eine abweichende Länge ist ein
/// <em>Fehler</em> und kein Anlass zu raten — falsch zuzuordnen hiesse, das
/// Profil der falschen Person zu zeigen.</para>
/// </remarks>
public interface IEinwilligungstor
{
    /// <summary>Eine Antwort je Mensch, in der Reihenfolge der Fragen.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    Task<IReadOnlyList<bool>> DarfSehenAlleAsync(
        IReadOnlyList<SubjectId> wer,
        TenantId firma,
        CancellationToken cancellationToken = default);
}
