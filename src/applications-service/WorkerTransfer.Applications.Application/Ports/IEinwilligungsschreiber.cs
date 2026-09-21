using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet. Kein Ergebnis — ein Systemzustand.</summary>
/// <remarks>
/// Bewusst kein Regelverstoß: eine JobApplication ohne die zugehörige Release wäre
/// eine, die das Unternehmen nicht lesen kann — und andersherum wäre es
/// schlimmer. Der Endpunkt macht daraus 503, was weder „abgeschickt" noch
/// „abgelehnt" ist.
/// </remarks>
public sealed class ConsentGateSilent(string reason, Exception? ursache = null)
    : Exception(reason, ursache);

/// <summary>Die Freigabeschlüssel, an genau einer Job gebaut.</summary>
public static class ConsentKey
{
    /// <summary>
    /// Welche Freigaben eine JobApplication erteilt.
    /// </summary>
    /// <remarks>
    /// Das Profile ist immer dabei — eine JobApplication ohne jede Angabe zur Person
    /// ist keine. CurriculumVitae und Portfolio nur, wenn die Person sie
    /// mitschickt. Jede nennt <em>diesen einen</em> Empfänger, nie „alle":
    /// wer sich bei einem Unternehmen bewirbt, hat den anderen nichts gesagt.
    /// </remarks>
    public static IReadOnlyList<string> Fuer(
        TenantId company, bool resume, bool portfolio, bool documents = false)
    {
        var key = new List<string> { $"profile.visibility:tenant:{company}" };

        if (resume)
        {
            key.Add($"resume.visibility:tenant:{company}");
        }

        if (portfolio)
        {
            key.Add($"portfolio.visibility:tenant:{company}");
        }

        if (documents)
        {
            // EIGENE Fähigkeit und nicht unter `resume` mitgeführt: Zeugnisse
            // und Zertifikate sind eine andere Datenklasse als der Werdegang,
            // und wer sie einzeln zurückziehen will, muss das können. Eine
            // Fähigkeit, die zwei Dinge freigibt, lässt sich nur ganz oder gar
            // nicht widerrufen.
            key.Add($"documents.visibility:tenant:{company}");
        }

        return key;
    }

    /// <summary>Alles, was eine JobApplication je erteilt haben kann.</summary>
    /// <remarks>
    /// Der Rückzug widerruft <em>bedingungslos</em> alle drei, auch die, die
    /// vielleicht nie erteilt wurden. Das schließt die Lücke, die ein
    /// geglückter Ledger-Aufruf mit fehlgeschlagenem Commit hinterlassen hätte,
    /// und der Ledger verträgt einen Revocation ohne vorherige Erteilung.
    /// </remarks>
    public static IReadOnlyList<string> Alles(TenantId company) =>
        Fuer(company, resume: true, portfolio: true, documents: true);
}

/// <summary>Schreibt in den Ledger — und liest nie aus ihm.</summary>
/// <remarks>
/// Dass hier nur geschrieben wird, ist keine Lücke: eine JobApplication enthält
/// keine Profildaten, sondern nennt eine <c>subject_id</c>. Wer Profile,
/// CurriculumVitae oder Portfolio sehen will, fragt die zuständigen Dienste, und
/// dort greift der Ledger. Eine Leseprüfung hier wäre eine zweite Job, die
/// dieselbe Frage beantwortet — und irgendwann anders.
/// </remarks>
public interface IConsentWriter
{
    /// <summary>Erteilt die Freigaben, die diese JobApplication mitbringt.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task GrantAsync(
        SubjectId subject,
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default);

    /// <summary>Nimmt sie zurück.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger hat nicht geantwortet.</exception>
    Task RevokeAsync(
        SubjectId subject,
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default);
}
