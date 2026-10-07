using Noelia.Core.Identity;
using WorkerTransfer.Messages.Domain.Conversations;

namespace WorkerTransfer.Messages.Application.Ports;

/// <summary>Who belongs to a process, as its own service says.</summary>
/// <param name="Person">The natural person.</param>
/// <param name="Company">The company.</param>
/// <param name="PersonName">
/// The name the company already holds from the process — used for nothing but
/// a placeholder, so it never reaches a model (E2).
/// </param>
public sealed record ProcessParties(SubjectId Person, TenantId Company, string PersonName);

/// <summary>Asks the process's own service who belongs to it.</summary>
/// <remarks>
/// <strong>With the caller's token, synchronously, never cached</strong>
/// (ADR-0047). Whoever that service does not show the process to does not see
/// the conversation either — and a member removed from the company stops at
/// the next request.
/// </remarks>
public interface IProcessLookup
{
    /// <summary>The parties, or <c>null</c> when the caller may not see the process.</summary>
    /// <exception cref="ProcessLookupSilent">The process's service did not answer.</exception>
    Task<ProcessParties?> PartiesAsync(string kind, Guid reference, CancellationToken cancellationToken = default);
}

/// <summary>The process's service did not answer — 503, never 404.</summary>
/// <remarks>
/// "Not yours" and "we do not know" are two statements. A 404 here would tell a
/// person their application is gone while applications-service is restarting.
/// </remarks>
public sealed class ProcessLookupSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);

/// <summary>A company's members — the recipients when the person writes.</summary>
public interface ICompanyMemberLookup
{
    /// <summary>All members of the company.</summary>
    /// <exception cref="ProcessLookupSilent">identity-service did not answer.</exception>
    Task<IReadOnlyList<SubjectId>> MembersAsync(TenantId company, CancellationToken cancellationToken = default);
}

/// <summary>One sent message, as the model sees it.</summary>
public sealed record HistoryEntry(Side Side, string Text);

/// <summary>What may go to the model for a reply draft — and therefore what may not.</summary>
/// <remarks>
/// <para><strong>This type IS the boundary</strong> (ADR-0024, ADR-0047): the
/// side that writes, the sent messages of this one conversation, the language
/// — and nothing from a profile, a CV, another conversation or a draft. A test
/// pins the field list.</para>
///
/// <para>The person's name travels with it for exactly one purpose: to be
/// replaced by a placeholder before anything leaves.</para>
/// </remarks>
public sealed record ReplyContext(
    Side Writing,
    IReadOnlyList<HistoryEntry> History,
    string PersonName,
    string Language)
{
    /// <summary>The rules above every reply draft.</summary>
    /// <remarks>
    /// <para><strong>German</strong>, like the cover letter's: it tells the model
    /// in which language to write, and its wording is what is measured.</para>
    ///
    /// <para><strong>Rule 3 is VISION-FRAGEN D2/D3.</strong> A model that
    /// judges the person for a company is Annex III no. 4 with no exception.
    /// It may help a side say what that side wants to say — nothing about
    /// the other side.</para>
    /// </remarks>
    public const string Rules = """
        Du entwirfst eine Antwort in einem Gespraech zwischen einer Person und
        einem Unternehmen ueber eine Bewerbung. Ein Mensch liest den Entwurf,
        aendert ihn und schickt ihn selbst ab.

        1. Schreib nur fuer die angegebene Seite, in der Ich- oder Wir-Form.
        2. Erfinde nichts: keine Termine, keine Zusagen, keine Zahlen, keine
           Namen, keine Fakten, die nicht im Gespraech stehen. Fehlt etwas,
           frag hoeflich danach.
        3. Bewerte die andere Seite nicht — keine Einschaetzung ihrer Eignung,
           ihres Charakters, ihrer Leistung, keine Zahl ueber einen Menschen.
        4. Keine Gesundheitsdaten, keine Religion, keine Herkunft, keine
           Gewerkschaft — auch wenn sie im Gespraech vorkommen.
        5. Angaben in eckigen Klammern wie [PERSON_NAME] sind Platzhalter.
           Uebernimm sie zeichengenau, ersetze sie nie durch einen Namen.
        6. Hoechstens 150 Woerter. Kein Betreff, keine Signatur ausser
           [PERSON_NAME] auf der Seite der Person.
        7. Sprache: die des bisherigen Gespraechs; ohne Gespraech die
           angegebene (de, en oder fr). Grammatisch korrekt.
        """;
}

/// <summary>The model is not set up, or does not answer.</summary>
public sealed class DraftUnavailable(string message) : Exception(message)
{
    /// <summary>The one sentence for "nothing is set up".</summary>
    public const string NoProvider = "No draft provider is set up.";
}

/// <summary>Drafts a reply — on request, once, and keeps nothing but the draft.</summary>
public interface IReplyDrafter
{
    /// <summary>A draft for <paramref name="context"/>'s side.</summary>
    /// <param name="requestedBy">
    /// Who asked. On the person's side their own AI access is used, if they set
    /// one up; otherwise and on the company's side the platform's.
    /// </param>
    /// <exception cref="DraftUnavailable">No provider, or it is silent.</exception>
    Task<string> DraftAsync(
        ReplyContext context, SubjectId requestedBy, CancellationToken cancellationToken = default);
}

/// <summary>The person's own wishes from her mandate (advisor-service).</summary>
public sealed record OwnWishes(string? EntryMonth, int? SalaryMin, int? SalaryMax, int? WorkloadPercent);

/// <summary>What the person herself stated — fetched with HER token (ADR-0049).</summary>
/// <param name="Wishes">Her mandate, or <c>null</c> when she wrote none.</param>
/// <param name="WishesUnknown">advisor-service did not answer — the advisor says so.</param>
/// <param name="EarliestStart">From her notice period (H2), or <c>null</c> when not stated.</param>
/// <param name="NoticeUnknown">transfer-service did not answer — the advisor says so.</param>
public sealed record OwnFacts(OwnWishes? Wishes, bool WishesUnknown, DateOnly? EarliestStart, bool NoticeUnknown);

/// <summary>Fetches the person's own facts for the advisor.</summary>
/// <remarks>Never throws for a silent service: the advice goes on without the fact and says it is missing.</remarks>
public interface IOwnFactsLookup
{
    /// <summary>Her own stated wishes and earliest start.</summary>
    Task<OwnFacts> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the AI advisor may read — and therefore what it may not (ADR-0049).</summary>
/// <remarks>
/// <para><strong>This type IS the boundary:</strong> the sent messages of
/// one conversation, her own facts, the language. No profile, no CV, nothing
/// about the company from elsewhere. A test pins the field list.</para>
/// </remarks>
public sealed record AdviceContext(
    IReadOnlyList<HistoryEntry> History,
    string PersonName,
    OwnFacts Facts,
    string Language)
{
    /// <summary>The rules above every piece of advice.</summary>
    /// <remarks>
    /// <para>German, like the other prompts; it tells the model the language.</para>
    /// <para><strong>Rule 3 is § 2 RDG, rule 4 ADR-0022, rule 5 the source
    /// rule of B5.</strong> Each is the sentence somebody would otherwise
    /// remove to make the advice "more helpful".</para>
    /// </remarks>
    public const string Rules = """
        Du bist der KI-Berater der Person, und nur ihrer. Du sprichst zu ihr,
        nicht zur Firma. Sie entscheidet; du hilfst ihr, klar zu sehen.

        1. Sag kurz, wo das Gespraech steht und was offen ist.
        2. Vergleiche das Gespraech mit IHREN eigenen Angaben (Wunsch-Eintritt,
           Gehaltswunsch, Pensum, fruehester Wechsel). Widerspricht etwas, sag
           es deutlich.
        3. Kein Rechtsrat: erklaere, was eine Klausel allgemein bedeutet, und
           schreib dazu "Lass das vor dem Unterschreiben pruefen". Beurteile
           nie, ob eine Klausel wirksam oder gut ist.
        4. Keine Zahl ueber die Person, kein "du bist X wert", keine
           Gehaltszahl, die nicht in den Angaben oder im Gespraech steht. Fuer
           Vergleiche verweise auf den Gehaltsvergleich der Plattform.
        5. Nenne zu jeder Angabe ihre Quelle: (Gespraech), (dein Mandat),
           (deine Kuendigungsfrist).
        6. Fehlt eine Angabe oder war sie nicht abrufbar, sag das, statt zu
           raten.
        7. Schlag hoechstens zwei naechste Schritte vor. Keine Gesundheit,
           keine Religion, keine Herkunft.
        8. Angaben in eckigen Klammern wie [PERSON_NAME] sind Platzhalter;
           uebernimm sie unveraendert.
        9. Hoechstens 200 Woerter, in der angegebenen Sprache.
        """;
}

/// <summary>The AI advisor (ADR-0049) — on request, once, and keeps nothing.</summary>
public interface IAdvisor
{
    /// <summary>Advice for the person about one conversation.</summary>
    /// <exception cref="DraftUnavailable">No provider, or it is silent.</exception>
    Task<string> AdviseAsync(AdviceContext context, SubjectId requestedBy, CancellationToken cancellationToken = default);
}
