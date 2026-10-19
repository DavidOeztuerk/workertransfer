using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Application.Ports;

/// <summary>The provider did not answer — or none is set up.</summary>
/// <param name="message">The kind of the failure, never its content.</param>
public sealed class DraftUnavailable(string message) : Exception(message);

/// <summary>
/// What may go into the prompt — and with it: what may not (ADR-0024).
/// </summary>
/// <param name="Title">What the company wrote itself.</param>
/// <param name="Description">Likewise.</param>
/// <param name="Skills">What it asks for itself.</param>
/// <param name="Location">Where.</param>
/// <param name="Wish">What it wants changed ("shorter", "more concrete").</param>
/// <remarks>
/// <b>This class IS the boundary.</b> It carries what the company wrote about
/// its own advert, and nothing else: <b>no <c>tenant_id</c>, no company
/// name</b> — and certainly nothing about a human being.
/// <para>
/// That is the reason this draft may be built at all: it helps a company phrase
/// <em>its own</em> advert and never says anything <em>about</em> anybody.
/// Scout, candidate evaluation, salary recommendation and team analysis all aim
/// at people and are therefore not here.
/// </para>
/// <para>
/// A context class of its own and no shared prompt with a branch: the rules for
/// a profile text and those for a job advert have nothing to do with each
/// other, and a branch is exactly the place where one day "invent nothing about
/// the person" would apply to an advert — or, worse, the other way round.
/// </para>
/// </remarks>
public sealed record AdvertDraft(
    string Title,
    string Description,
    IReadOnlyList<string> Skills,
    string Location,
    string Wish)
{
    /// <summary>The wish, checked.</summary>
    /// <remarks>
    /// The check stands here and not at the endpoint, because this type is the
    /// boundary — a second call site would otherwise not get it. Title,
    /// description, location and skills are already value objects of the domain
    /// and limited there; the wish comes raw from the body of the request.
    /// </remarks>
    public string Wish { get; init; } =
        Wish is { Length: > Job.MaxWishLength }
            ? throw new WishError()
            : Wish;

    /// <summary>What the model has to keep to.</summary>
    /// <remarks>
    /// <strong>The rules and the prompt labels stay German.</strong> They are
    /// what the model receives, not code: the platform speaks German to the
    /// company, the model is told to answer in German, and an English
    /// instruction would change the drafts people get.
    /// </remarks>
    public static string Rules =>
        "Du hilfst einem Unternehmen, seine eigene Stellenanzeige zu "
        + "formulieren. Schreibe auf Deutsch, sachlich und ohne Werbesprache.\n"
        + "Regeln:\n"
        + "- Erfinde NICHTS hinzu. Benutze nur, was unten steht.\n"
        + "- Keine Aussagen über Bewerberinnen oder Bewerber, keine "
        + "Anforderungen an Persönlichkeit, kein Alter, kein Geschlecht.\n"
        + "- Keine Superlative und keine Behauptungen über das Unternehmen, die "
        + "nicht unten stehen.\n"
        + "- Höchstens 250 Wörter.\n"
        + "- Gib nur den Text zurück, ohne Anrede und ohne Erklärung.";

    /// <summary>The prompt, word for word — so a test can check it.</summary>
    public string Prompt =>
        string.Join('\n', new[]
        {
            Title.Length > 0 ? $"Titel: {Title}" : "",
            Description.Length > 0 ? $"Bisheriger Text: {Description}" : "",
            Skills.Count > 0 ? $"Anforderungen: {string.Join(", ", Skills)}" : "",
            Location.Length > 0 ? $"Ort: {Location}" : "",
            Wish.Length > 0 ? $"Wunsch: {Wish}" : ""
        }.Where(part => part.Length > 0));
}

/// <summary>Writes a draft and keeps nothing of it.</summary>
public interface IDrafter
{
    /// <exception cref="DraftUnavailable">No provider, or it stays silent.</exception>
    Task<string> DraftAsync(
        AdvertDraft draft, CancellationToken cancellationToken = default);
}
