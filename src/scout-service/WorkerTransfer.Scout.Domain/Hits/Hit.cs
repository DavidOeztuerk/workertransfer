using Noelia.Core.Identity;

namespace WorkerTransfer.Scout.Domain.Hits;

/// <summary>A tick: did the person name this word?</summary>
/// <remarks>
/// <para><strong>A yes or a no per word — and nowhere a sum over them.</strong>
/// "2 of 3" would be a number about a human being (ADR-0022, ADR-0036
/// condition 2), and it would hide exactly the only thing that helps:
/// <em>which</em> skill is missing. A percentage even more so — it looks like a
/// measurement and is a weighting nobody has justified.</para>
///
/// <para>A tick can also be <em>contradicted</em>: it names a word and an
/// answer, and whoever thinks it wrong can say why. A number cannot be
/// disputed.</para>
/// </remarks>
/// <param name="Word">The word that was searched for — canonical.</param>
/// <param name="Named">Whether the person typed it into their profile themselves.</param>
public sealed record Tick(string Word, bool Named);

/// <summary>Where a piece of evidence comes from.</summary>
/// <remarks>
/// Evidence is a statement about an <em>artefact</em>, never about a human being
/// (ADR-0033). The origin therefore stands beside it: whoever reads the evidence
/// is to see who wrote it — a human being on a repository, or GitHub's language
/// detection on its files.
/// </remarks>
public enum EvidenceKind
{
    /// <summary>A topic a human being wrote onto the repository.</summary>
    /// <remarks>
    /// The strongest evidence, because it is a <em>naming</em> and nothing
    /// derived (ADR-0033).
    /// </remarks>
    Topic,

    /// <summary>A language that occurs in the repository.</summary>
    /// <remarks>
    /// The set, never the share. GitHub reports a byte count per language, and
    /// exactly from that the deleted package computed its "skill" (ADR-0022 §2).
    /// The numbers travel nowhere.
    /// </remarks>
    Language
}

/// <summary>A single piece of evidence with its origin.</summary>
/// <param name="Word">The topic or the language name, copied.</param>
/// <param name="Kind">Who said it.</param>
/// <param name="Project">The repository it comes from.</param>
/// <param name="Url">The link there — evidence that cannot be checked is none.</param>
public sealed record Evidence(string Word, EvidenceKind Kind, string Project, string Url);

/// <summary>What a hit says about its own incompleteness.</summary>
/// <remarks>
/// <para><strong>The third decision of ADR-0036, and it is the most
/// uncomfortable one.</strong> Whoever has nothing on GitHub is not worse but
/// elsewhere (ADR-0022 §3). A hit without evidence therefore gets the same
/// answer shape as every other and a <em>hint in the JSON</em> — no empty box,
/// no greyed-out card, no leaving out.</para>
///
/// <para>A word and no sentences: the UI phrases it in the reader's language
/// (ADR-0031), and a German sentence on the wire would be a language the server
/// fixes for everybody.</para>
/// </remarks>
public enum EvidenceState
{
    /// <summary>Evidence is there, and it is complete.</summary>
    Complete,

    /// <summary>This person has no proven, released connection.</summary>
    /// <remarks>
    /// That is <em>no</em> judgement. It means: nothing stands here, and nor does
    /// it stand here that there is nothing.
    /// </remarks>
    NoneReleased,

    /// <summary>There is evidence, but not all of it could be fetched.</summary>
    Partial,

    /// <summary>The evidence service did not answer.</summary>
    /// <remarks>
    /// Expressly different from <see cref="NoneReleased"/>: "we do not know right
    /// now" is something other than "there is nothing", and naming the two alike
    /// would be a statement about the person that stems from our outage.
    /// </remarks>
    Unavailable
}

/// <summary>A human being who was found — with ticks and evidence, without a number.</summary>
/// <remarks>
/// <para>What stands here the person wrote themselves (<see cref="Headline"/>,
/// <see cref="Location"/>, <see cref="Named"/>) or is evidence with origin and
/// link. Nothing is computed, nothing summarised, nothing ordered.</para>
///
/// <para><strong>The tick list belongs in this type and not in the UI
/// alone</strong> (ADR-0036 decision 3). If it lay only there, the browser would
/// compute a number from the raw data — and ADR-0022 would be back through the
/// back door.</para>
/// </remarks>
public sealed record Hit(
    SubjectId Subject,
    string Headline,
    string Bio,
    string Location,
    bool RemoteOk,
    IReadOnlyList<string> Named,
    IReadOnlyList<Tick> Ticks,
    IReadOnlyList<Evidence> Evidence,
    EvidenceState EvidenceState,
    /// <summary>The tick for the distance (ADR-0041) — never a number.</summary>
    Availability Availability);

/// <summary>A page of hits.</summary>
/// <remarks>
/// <para><strong>No total</strong> (ADR-0026, ADR-0036). Through the difference
/// to the page length it would reveal how many profiles are <em>not</em>
/// released — exactly the information the ledger protects.</para>
///
/// <para>And <strong>no filling up of a short page</strong> (ADR-0020 §4):
/// loading more until the page is full would reveal the same through the number
/// of rounds. A short page is a correct answer here.</para>
/// </remarks>
public sealed record HitPage(IReadOnlyList<Hit> Items, string? Next);
