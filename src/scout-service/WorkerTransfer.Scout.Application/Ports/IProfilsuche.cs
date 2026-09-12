using Girder.Core.Identity;
using WorkerTransfer.Scout.Domain.Suchen;
using WorkerTransfer.Scout.Domain.Treffer;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>Die Profilsuche hat nicht geantwortet.</summary>
/// <remarks>
/// Wie beim Ledger: die Art, nie der Inhalt — und kein leeres Ergebnis. „Wir
/// haben gerade nicht gefunden" ist etwas anderes als „es gibt niemanden", und
/// eine leere Liste sähe aus wie die zweite Aussage.
/// </remarks>
/// <param name="grund">Die Art des Fehlschlags.</param>
public sealed class ProfilsucheSchweigt(string grund) : Exception(grund);

/// <summary>Ein Profil, so wie profile-service es meldet.</summary>
/// <remarks>
/// <para>Nur, was die Person selbst geschrieben hat. Keine Adresse, kein Name,
/// kein Arbeitgeber — dieser Dienst braucht sie nicht und soll sie deshalb auch
/// nicht bekommen.</para>
///
/// <para><see cref="Genannt"/> ist die Herkunftsklasse <em>genannt</em> aus
/// ADR-0033 und die einzige durchsuchbare. Belege stehen hier nicht: sie werden
/// erst zum Treffer dazugeholt.</para>
/// </remarks>
public sealed record Profilfund(
    SubjectId Wer,
    string Ueberschrift,
    string Text,
    string Ort,
    bool RemoteMoeglich,
    IReadOnlyList<string> Genannt,
    /// <summary>Was sie über ihren Weg gesagt hat, oder <c>null</c> (ADR-0041).</summary>
    Pendelstufe? Pendelstufe = null);

/// <summary>Eine Seite Profile, in der Reihenfolge, die profile-service hält.</summary>
/// <remarks>
/// <strong>Ohne Gesamtzahl.</strong> Die Reihenfolge ist <c>updated_at DESC,
/// id DESC</c> — stabil und sachfremd, damit zwei gleiche Suchen dasselbe
/// liefern und aus der Reihenfolge nichts über einen Menschen folgt
/// (ADR-0036 Auflage 1).
/// </remarks>
public sealed record Profilfundseite(IReadOnlyList<Profilfund> Eintraege, string? Weiter);

/// <summary>Fragt profile-service nach Profilen, die die genannten Worte tragen.</summary>
/// <remarks>
/// <para><strong>Das Filtern bleibt dort, wo die Profile liegen</strong>
/// (ADR-0004: kein dienstübergreifender Speicher). Dieser Dienst hält keine
/// Kopie der Profile — eine Kopie veraltete gegen jede Änderung und wäre beim
/// ersten Widerruf eine zweite Wahrheit.</para>
///
/// <para><strong>Die Freigabe prüft profile-service hier ausdrücklich
/// nicht.</strong> Sie wird eine Zeile weiter oben gefragt, über
/// <see cref="IEinwilligungstor"/> und für die ganze Seite auf einmal — das
/// ist der harte Teil aus <c>GET /candidates</c>, mitgenommen und nicht neu
/// erfunden (ADR-0036 Entscheidung 1). Der Aufruf geht deshalb durch eine
/// Dienst-zu-Dienst-Tür hinter dem gemeinsamen Geheimnis und hat keine
/// Gateway-Route.</para>
/// </remarks>
public interface IProfilsuche
{
    /// <param name="filter">Nur genannte Worte, Ort, Remote.</param>
    /// <param name="seitenlaenge">Wie viele Zeilen höchstens.</param>
    /// <param name="zeiger">Wo es weitergeht, oder <c>null</c>.</param>
    /// <param name="cancellationToken">Bricht ab.</param>
    /// <exception cref="ProfilsucheSchweigt">profile-service antwortet nicht.</exception>
    Task<Profilfundseite> SucheAsync(
        Suchfilter filter,
        int seitenlaenge,
        string? zeiger,
        CancellationToken cancellationToken = default);

    /// <summary>Ein einzelnes Profil, oder <c>null</c>.</summary>
    /// <remarks>
    /// Fuer die Ansprache, und ebenfalls <strong>ohne</strong> Freigabepruefung
    /// in profile-service: die steht eine Zeile darueber im Handler, damit es
    /// im ganzen Dienst genau EINE Stelle gibt, an der ueber Sichtbarkeit
    /// entschieden wird. Zwei Stellen waeren zwei Wahrheiten, und die stille
    /// Variante davon zeigt ein Profil, das niemand freigegeben hat.
    /// </remarks>
    /// <exception cref="ProfilsucheSchweigt">profile-service antwortet nicht.</exception>
    Task<Profilfund?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
