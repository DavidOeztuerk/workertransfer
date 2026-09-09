using Girder.Core.Identity;

namespace WorkerTransfer.Profile.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.</summary>
/// <remarks>
/// Ausdrücklich kein Eingabefehler und kein fachlicher Ausgang, sondern ein
/// Systemzustand. Der Endpunkt macht daraus 503: weder 404 noch das Profil
/// zeigen, denn beides wäre eine Behauptung über einen Menschen, die in diesem
/// Augenblick niemand aufstellen kann (ADR-0020 §3).
/// </remarks>
/// <param name="meldung">Was schiefging — nie, worum es ging.</param>
public sealed class EinwilligungSchweigt(string meldung) : Exception(meldung);

/// <summary>Die Fähigkeiten, um die es bei einem Profil geht.</summary>
/// <remarks>
/// Zwei, und beide sind <em>Sichtbarkeiten</em>. Das ist der Grund, warum
/// „lösche <c>profile.visibility:public</c>“ nie „lösche das Profil“ heißen
/// kann (ADR-0027 §1): ein Widerruf der Sichtbarkeit ist kein Löschauftrag.
/// </remarks>
public static class Sichtbarkeiten
{
    /// <summary>„Für alle Unternehmen“ — der Schalter auf der Profilseite.</summary>
    public const string Oeffentlich = "profile.visibility:public";

    /// <summary>„Für dieses eine Unternehmen“ — entsteht mit einer Bewerbung.</summary>
    /// <remarks>
    /// Die additive Verfeinerung aus ADR-0020: <c>:public</c> bleibt, was es
    /// war, hier kommt eine zweite Möglichkeit dazu — keine Lockerung der
    /// ersten.
    /// </remarks>
    /// <param name="firma">Das Unternehmen aus dem Token des Aufrufers.</param>
    public static string FuerFirma(TenantId firma) => $"profile.visibility:tenant:{firma.Value}";
}

/// <summary>Darf der Aufrufer das Profil dieser Person sehen?</summary>
/// <remarks>
/// Ein Port und kein Import: die Handler sollen wissen, <em>dass</em> sie fragen
/// müssen, nicht <em>wie</em>. Der HTTP-Adapter liegt in der Infrastruktur, und
/// dort liegt auch, womit die Frage beglaubigt wird — der Dienst fragt im
/// Auftrag des Aufrufers, nicht mit einem eigenen Konto.
/// <para>
/// Wird bei jedem Fremdabruf gefragt, synchron und ohne jeden Zwischenspeicher
/// (ADR-0013). Ein Widerruf muss beim nächsten Lesen wirken, nicht beim
/// übernächsten — ein Cache wäre hier kein Leistungsdetail, sondern ein
/// Regelbruch.
/// </para>
/// </remarks>
public interface IEinwilligungstor
{
    /// <summary>Die Frage für einen Menschen.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    Task<bool> DarfSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Dieselbe Frage für viele — eine Antwort je Eingabe, in deren Reihenfolge.</summary>
    /// <remarks>
    /// Eine eigene Methode und nicht <see cref="DarfSehenAsync(SubjectId,TenantId,CancellationToken)"/>
    /// mit einer Liste: die einzelne Frage hat einen anderen Aufrufer und darf
    /// nicht teurer werden, weil die Liste billiger geworden ist.
    /// <para>
    /// Gemessen war das der Grund für die Sammelfrage: eine Seite von zwanzig
    /// Profilen kostete bis zu vierzig einzelne Aufrufe, jeder mit eigenem
    /// Verbindungsaufbau — 1,7 bis 8,8 Sekunden je Seite, und unter Last mehr,
    /// als die Oberfläche abwartet. Danach 0,015 bis 0,083 Sekunden, bei
    /// gleichem Ergebnis.
    /// </para>
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">
    /// Der Ledger antwortet nicht — oder er antwortet in anderer Zahl als
    /// gefragt wurde. Ein <c>false</c> wäre auch hier eine Aussage über einen
    /// Menschen, die niemand treffen kann.
    /// </exception>
    Task<IReadOnlyList<bool>> DarfSehenAlleAsync(
        IReadOnlyList<SubjectId> wer, TenantId firma, CancellationToken cancellationToken = default);
}
