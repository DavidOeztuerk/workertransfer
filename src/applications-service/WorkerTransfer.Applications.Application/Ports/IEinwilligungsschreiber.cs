using Girder.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet. Kein Ergebnis — ein Systemzustand.</summary>
/// <remarks>
/// Bewusst kein Regelverstoß: eine Bewerbung ohne die zugehörige Freigabe wäre
/// eine, die das Unternehmen nicht lesen kann — und andersherum wäre es
/// schlimmer. Der Endpunkt macht daraus 503, was weder „abgeschickt" noch
/// „abgelehnt" ist.
/// </remarks>
public sealed class EinwilligungSchweigt(string grund, Exception? ursache = null)
    : Exception(grund, ursache);

/// <summary>Die Freigabeschlüssel, an genau einer Stelle gebaut.</summary>
public static class Einwilligungsschluessel
{
    /// <summary>
    /// Welche Freigaben eine Bewerbung erteilt.
    /// </summary>
    /// <remarks>
    /// Das Profil ist immer dabei — eine Bewerbung ohne jede Angabe zur Person
    /// ist keine. Lebenslauf und Portfolio nur, wenn die Person sie
    /// mitschickt. Jede nennt <em>diesen einen</em> Empfänger, nie „alle":
    /// wer sich bei einem Unternehmen bewirbt, hat den anderen nichts gesagt.
    /// </remarks>
    public static IReadOnlyList<string> Fuer(
        TenantId firma, bool lebenslauf, bool portfolio, bool unterlagen = false)
    {
        var schluessel = new List<string> { $"profile.visibility:tenant:{firma}" };

        if (lebenslauf)
        {
            schluessel.Add($"resume.visibility:tenant:{firma}");
        }

        if (portfolio)
        {
            schluessel.Add($"portfolio.visibility:tenant:{firma}");
        }

        if (unterlagen)
        {
            // EIGENE Fähigkeit und nicht unter `resume` mitgeführt: Zeugnisse
            // und Zertifikate sind eine andere Datenklasse als der Werdegang,
            // und wer sie einzeln zurückziehen will, muss das können. Eine
            // Fähigkeit, die zwei Dinge freigibt, lässt sich nur ganz oder gar
            // nicht widerrufen.
            schluessel.Add($"documents.visibility:tenant:{firma}");
        }

        return schluessel;
    }

    /// <summary>Alles, was eine Bewerbung je erteilt haben kann.</summary>
    /// <remarks>
    /// Der Rückzug widerruft <em>bedingungslos</em> alle drei, auch die, die
    /// vielleicht nie erteilt wurden. Das schließt die Lücke, die ein
    /// geglückter Ledger-Aufruf mit fehlgeschlagenem Commit hinterlassen hätte,
    /// und der Ledger verträgt einen Widerruf ohne vorherige Erteilung.
    /// </remarks>
    public static IReadOnlyList<string> Alles(TenantId firma) =>
        Fuer(firma, lebenslauf: true, portfolio: true, unterlagen: true);
}

/// <summary>Schreibt in den Ledger — und liest nie aus ihm.</summary>
/// <remarks>
/// Dass hier nur geschrieben wird, ist keine Lücke: eine Bewerbung enthält
/// keine Profildaten, sondern nennt eine <c>subject_id</c>. Wer Profil,
/// Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste, und
/// dort greift der Ledger. Eine Leseprüfung hier wäre eine zweite Stelle, die
/// dieselbe Frage beantwortet — und irgendwann anders.
/// </remarks>
public interface IEinwilligungsschreiber
{
    /// <summary>Erteilt die Freigaben, die diese Bewerbung mitbringt.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task ErteileAsync(
        SubjectId wer,
        IReadOnlyList<string> faehigkeiten,
        CancellationToken cancellationToken = default);

    /// <summary>Nimmt sie zurück.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task WiderrufeAsync(
        SubjectId wer,
        IReadOnlyList<string> faehigkeiten,
        CancellationToken cancellationToken = default);
}
