using Noelia.Core.Identity;

namespace WorkerTransfer.Profile.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.</summary>
/// <remarks>
/// Ausdrücklich kein Eingabefehler und kein fachlicher Ausgang, sondern ein
/// Systemzustand. Der Endpunkt macht daraus 503: weder 404 noch das PersonProfile
/// zeigen, denn beides wäre eine Behauptung über einen Menschen, die in diesem
/// Augenblick niemand aufstellen kann (ADR-0020 §3).
/// </remarks>
/// <param name="meldung">Was schiefging — nie, worum es ging.</param>
public sealed class ConsentGateSilent(string notification) : Exception(notification);

/// <summary>Die Fähigkeiten, um die es bei einem PersonProfile geht.</summary>
/// <remarks>
/// Zwei, und beide sind <em>Sichtbarkeiten</em>. Das ist der Grund, warum
/// „lösche <c>profile.visibility:public</c>“ nie „lösche das PersonProfile“ heißen
/// kann (ADR-0027 §1): ein Revocation der Sichtbarkeit ist kein Löschauftrag.
/// </remarks>
public static class Sichtbarkeiten
{
    /// <summary>„Für alle Unternehmen“ — der Schalter auf der ProfilePage.</summary>
    public const string Oeffentlich = "profile.visibility:public";

    /// <summary>„Für dieses eine Unternehmen“ — entsteht mit einer JobApplication.</summary>
    /// <remarks>
    /// Die additive Verfeinerung aus ADR-0020: <c>:public</c> bleibt, was es
    /// war, hier kommt eine zweite Möglichkeit dazu — keine Lockerung der
    /// ersten.
    /// </remarks>
    /// <param name="firma">Das Unternehmen aus dem Token des Aufrufers.</param>
    public static string FuerFirma(TenantId company) => $"profile.visibility:tenant:{company.Value}";
}

/// <summary>Darf der Aufrufer das PersonProfile dieser Person sehen?</summary>
/// <remarks>
/// Ein Port und kein Import: die Handler sollen wissen, <em>dass</em> sie fragen
/// müssen, nicht <em>wie</em>. Der HTTP-Adapter liegt in der Infrastruktur, und
/// dort liegt auch, womit die Frage beglaubigt wird — der Dienst fragt im
/// Auftrag des Aufrufers, nicht mit einem eigenen Konto.
/// <para>
/// Wird bei jedem Fremdabruf gefragt, synchron und ohne jeden Zwischenspeicher
/// (ADR-0013). Ein Revocation muss beim nächsten Lesen wirken, nicht beim
/// übernächsten — ein Cache wäre hier kein Leistungsdetail, sondern ein
/// Regelbruch.
/// </para>
/// </remarks>
public interface IConsentGate
{
    /// <summary>Die Frage für einen Menschen.</summary>
    /// <remarks>
    /// <strong>Nur noch die einzelne Frage.</strong> Daneben stand eine
    /// Sammelfrage, und sie stand dort für <c>GET /candidates</c>: eine Seite
    /// von zwanzig Profilen kostete sonst bis zu vierzig einzelne Aufrufe.
    /// Diese Route ist am 11.09.2026 gefallen, und die Sammelfrage mit ihr —
    /// gesammelt fragt jetzt scout-service, für seine eigene Seite. Zwei
    /// Sammelfragen an denselben Ledger wären zwei Jobs, an denen über
    /// Sichtbarkeit entschieden wird.
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);
}
