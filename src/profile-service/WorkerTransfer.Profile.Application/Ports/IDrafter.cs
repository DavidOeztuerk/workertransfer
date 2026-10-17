using Noelia.Core.Identity;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Ports;

/// <summary>The provider did not answer — or none is set up.</summary>
/// <remarks>
/// An error of its own and <b>no silent fallback to a template</b>: a draft
/// that does not come from the model but looks like it would be the worse
/// answer. The message never carries the prompt and never the answer.
/// </remarks>
/// <param name="message">The kind of the failure, never its content.</param>
public sealed class DraftUnavailable(string message) : Exception(message);

/// <summary>
/// What may go into the prompt — and with it: what may not (ADR-0024).
/// </summary>
/// <param name="Headline">What the person wrote as their headline themselves.</param>
/// <param name="Text">What they wrote about themselves.</param>
/// <param name="Skills">What they named themselves.</param>
/// <param name="Wish">What they want changed ("shorter", "more matter-of-fact").</param>
/// <remarks>
/// <b>This class IS the boundary.</b> It carries what the person wrote about
/// themselves, and nothing else: no name, no email address, no
/// <c>SubjectId</c>, no employer, no resume, no application, no market status.
/// <para>
/// A record with named fields and no free dictionary, because a dictionary
/// silently carries one key more at the next feature — and nobody sees it when
/// reading.
/// </para>
/// </remarks>
public sealed record DraftSituation(
    string Headline,
    string Text,
    IReadOnlyList<string> Skills,
    string Wish)
{
    /// <summary>The wish, checked.</summary>
    /// <remarks>
    /// The check stands here and not at the endpoint, because this type is the
    /// boundary: a second call site — a batch job, a second endpoint — would
    /// otherwise not get it. And it is the only length in this class that has
    /// to be checked: headline, text and skills come from the stored profile
    /// and are long since limited there, the wish by contrast comes raw from
    /// the body of the request.
    /// </remarks>
    public string Wish { get; init; } =
        Wish is { Length: > PersonProfile.MaxWishLength }
            ? throw new WishError()
            : Wish;

    /// <summary>What the model has to keep to.</summary>
    /// <remarks>
    /// "Write AS the person" is the core: the draft is a suggestion for their
    /// own text, no description from outside. Hence also the ban on inventing
    /// anything — a skill in the profile that the person never named is a false
    /// statement about them, and they may notice it only in the interview.
    /// <para>
    /// A rule set of its own per kind of draft, instead of a shared prompt with
    /// a branch: the rules for a profile text and those for a job advert have
    /// nothing to do with each other, and a branch is the place where some day
    /// a rule applies to the wrong side.
    /// </para>
    /// <para>
    /// <strong>The rules and the prompt labels stay German.</strong> They are
    /// what the model receives, not code: the platform speaks German to the
    /// person, the model is told to answer in German, and an English
    /// instruction would change the drafts people get.
    /// </para>
    /// </remarks>
    public static string Rules =>
        "Du hilfst einer Person, ihren eigenen Profiltext auf einer "
        + "Job-Plattform zu formulieren. Schreibe in der Ich-Form, auf Deutsch, "
        + "sachlich und ohne Werbesprache.\n"
        + "Regeln:\n"
        + "- Erfinde NICHTS hinzu. Benutze nur, was unten steht. Wenn dort wenig "
        + "steht, schreibe wenig.\n"
        + "- Keine Superlative, keine Behauptungen über Erfahrung in Jahren, "
        + "keine Bewertung der Person.\n"
        + "- Höchstens 120 Wörter.\n"
        + "- Gib nur den Text zurück, ohne Anrede, ohne Überschrift, ohne "
        + "Erklärung.";

    /// <summary>The prompt, word for word — so a test can check it.</summary>
    /// <remarks>
    /// Separate from sending, because the interesting question is not whether
    /// HTTP works but <b>what goes out</b>.
    /// </remarks>
    public string Prompt =>
        string.Join('\n', new[]
        {
            Headline.Length > 0 ? $"Bisherige Überschrift: {Headline}" : "",
            Text.Length > 0 ? $"Bisheriger Text: {Text}" : "",
            Skills.Count > 0 ? $"Fähigkeiten: {string.Join(", ", Skills)}" : "",
            Wish.Length > 0 ? $"Wunsch der Person: {Wish}" : ""
        }.Where(part => part.Length > 0));
}

/// <summary>Writes a draft and keeps nothing of it.</summary>
/// <remarks>
/// One call, one text. No memory, no vector store, no plan-act-reflect: all
/// three either store or act unasked. And no entry in the consent ledger — it
/// would claim a standing permission nobody gave. <b>The button is the
/// consent</b>, informed and per use.
/// </remarks>
public interface IDrafter
{
    /// <exception cref="DraftUnavailable">
    /// No provider set up, or it does not answer.
    /// </exception>
    /// <param name="situation">What goes out — and only that.</param>
    /// <param name="requestedBy">Whose own key, if any, is used (ADR-0051). It never reaches the model.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task<string> DraftAsync(
        DraftSituation situation, SubjectId requestedBy, CancellationToken cancellationToken = default);
}
