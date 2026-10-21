using Noelia.Core.Identity;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>The provider did not answer — or none is set up.</summary>
/// <remarks>
/// An error of its own and <strong>no silent fallback to a template</strong>: a
/// draft that does not come from the model but looks like it would be the worse
/// answer — somebody would take it for a suggestion and send it off. The
/// message never carries the prompt and never the answer.
/// </remarks>
/// <param name="message">The kind of the failure, never its content.</param>
public sealed class DraftUnavailable(string message) : Exception(message);

/// <summary>
/// What may go into the prompt of an approach — and with it: what may not.
/// </summary>
/// <remarks>
/// <para><strong>This class IS the boundary</strong> (ADR-0024, ADR-0036
/// condition 4). It carries <em>only words that are already visible and
/// named</em>: what the approached person typed into their profile themselves
/// and what this company may see anyway, plus the words the company itself
/// searched for.</para>
///
/// <para>What is deliberately missing: name, email address, <c>SubjectId</c>,
/// employer, resume, market status, applications — and the <em>name of the
/// searching company</em>. The last point is the same as with the advert draft
/// (ADR-0024): a company name in the prompt would be the invitation to let the
/// model say something about the employer that nobody has checked.</para>
///
/// <para><strong>No evidence either.</strong> It is visible but not named: a
/// GitHub topic is a statement about a repository. Writing it into an approach
/// would mean attributing it to the person — exactly the step ADR-0033 reserves
/// for the person themselves.</para>
///
/// <para>A record with named fields and no free dictionary, because a
/// dictionary silently carries one key more at the next feature — and nobody
/// sees it when reading. A field-set test pins the set.</para>
///
/// <para>And a type of its <em>own</em> with rules of its <em>own</em>, no
/// shared prompt with an <c>if</c>: the rules for a profile text, for an advert
/// and for an approach have nothing to do with each other, and an <c>if</c> is
/// the place where some day a rule applies to the wrong side.</para>
/// </remarks>
/// <param name="Headline">What the person wrote as their headline themselves.</param>
/// <param name="Named">What they named as skills themselves.</param>
/// <param name="Searched">What the company searched for — its own words.</param>
/// <param name="Wish">What the human being wants changed about the draft.</param>
public sealed record ApproachSituation(
    string Headline,
    IReadOnlyList<string> Named,
    IReadOnlyList<string> Searched,
    string Wish)
{
    /// <summary>How long the wish may be.</summary>
    public const int MaxWishLength = 300;

    /// <summary>The wish, checked.</summary>
    /// <remarks>
    /// The check stands here and not at the endpoint, because this type is the
    /// boundary: a second call site would otherwise not get it. And it is the
    /// only length in this class that has to be checked — everything else comes
    /// from stored fields that are long since limited.
    /// </remarks>
    public string Wish { get; init; } =
        Wish is { Length: > MaxWishLength }
            ? throw new InputError(
                $"a wish may not be longer than {MaxWishLength} characters")
            : Wish;

    /// <summary>What the model has to keep to.</summary>
    /// <remarks>
    /// <para>The core is "<em>write a question, not a judgement</em>". An
    /// approach is the first sentence of one human being to another they do not
    /// know; anything that sounds like a judgement about them is exactly what
    /// ADR-0022 keeps away — and the person could not even comment on it,
    /// because they never see the prompt.</para>
    ///
    /// <para>Hence also: no invented experience, no number, no "fits 80 %". And
    /// expressly the request <em>not</em> to pretend to know the person.</para>
    ///
    /// <para><strong>The rules and the prompt labels stay German.</strong> They
    /// are what the model receives, not code: the platform speaks German to the
    /// person, the model is told to answer in German, and an English instruction
    /// would change the drafts people get.</para>
    /// </remarks>
    public static string Rules =>
        "Du hilfst einem Menschen in einem Unternehmen, eine erste Nachricht an "
        + "eine Person auf einer Job-Plattform zu formulieren. Schreibe auf "
        + "Deutsch, sachlich, kurz und ohne Werbesprache.\n"
        + "Regeln:\n"
        + "- Erfinde NICHTS hinzu. Benutze nur, was unten steht.\n"
        + "- Beurteile die Person NICHT. Keine Einschaetzung, keine Zahl, kein "
        + "\"passt gut\", keine Behauptung ueber Jahre an Erfahrung.\n"
        + "- Tu nicht so, als kenntest du sie. Schreibe eine Frage, keine "
        + "Feststellung.\n"
        + "- Sag, worum es geht, und ueberlass ihr die Antwort.\n"
        + "- Hoechstens 120 Woerter.\n"
        + "- Gib nur den Text zurueck, ohne Betreff, ohne Unterschrift, ohne "
        + "Erklaerung.";

    /// <summary>The prompt, word for word — so a test can check it.</summary>
    /// <remarks>
    /// Separate from sending, because the interesting question is not whether
    /// HTTP works but <strong>what goes out</strong>. The labels are German for
    /// the reason given at <see cref="Rules"/>.
    /// </remarks>
    public string Prompt =>
        string.Join('\n', new[]
        {
            Headline.Length > 0 ? $"Ueberschrift der Person: {Headline}" : "",
            Named.Count > 0 ? $"Von ihr genannte Faehigkeiten: {string.Join(", ", Named)}" : "",
            Searched.Count > 0 ? $"Gesucht wurde nach: {string.Join(", ", Searched)}" : "",
            Wish.Length > 0 ? $"Wunsch des Absenders: {Wish}" : ""
        }.Where(part => part.Length > 0));
}

/// <summary>Writes a draft and keeps nothing of it.</summary>
/// <remarks>
/// <para><strong>A draft, no dispatch</strong> (ADR-0036 condition 4). This
/// service has no way of writing to anybody: no postman, no mail address, no
/// notification to the approached person. The text goes to the browser of
/// whoever asked and lies there in a form. Whoever sends it sends it themselves
/// — on a path that does not exist here yet.</para>
///
/// <para>One call, one text. No memory, no vector store, no plan-act-reflect:
/// all three either store or act unasked. And no entry in the consent ledger —
/// it would claim a standing permission nobody gave. <strong>The button is the
/// consent</strong>, informed and per use.</para>
/// </remarks>
public interface IDrafter
{
    /// <param name="situation">What goes into the prompt — and nothing else.</param>
    /// <param name="company">
    /// Whose model writes: the company's own key if it stored one, otherwise
    /// the platform's (ADR-0051). It selects the access and never reaches the
    /// prompt.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="DraftUnavailable">
    /// No provider set up, or it does not answer.
    /// </exception>
    Task<string> DraftAsync(
        ApproachSituation situation, TenantId company, CancellationToken cancellationToken = default);
}
