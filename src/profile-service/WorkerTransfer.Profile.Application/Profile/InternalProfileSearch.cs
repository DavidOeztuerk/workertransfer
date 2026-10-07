using MediatR;
using WorkerTransfer.Profile.Application.Messages;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>A page of profiles for another service — without asking the ledger.</summary>
/// <param name="PageLength">How many rows the page carries at most.</param>
/// <param name="Cursor">Where it continues, as text from the request.</param>
/// <param name="Skills">
/// The words searched for. Somebody must have <em>named</em> <strong>at least
/// one</strong> — OR, not AND.
/// </param>
/// <param name="Location">Part of the text.</param>
/// <param name="RemoteOnly">Only whoever expressly ticked remote.</param>
/// <remarks>
/// <para><strong>Without asking the ledger, and that is the most conspicuous
/// line of this type.</strong> It is not missing, it stands elsewhere:
/// scout-service asks the ledger for the whole page at once, because there is
/// to be exactly ONE place where visibility is decided (ADR-0036 decision 1).
/// Two places would be two truths — and the silent half of them shows profiles
/// nobody released.</para>
///
/// <para>That is why this query hangs on a door behind the shared secret and
/// has no gateway route. Whoever calls it is a service, and it still has to ask
/// the ledger.</para>
///
/// <para><strong>OR and not AND, unlike <c>/candidates</c>.</strong> Under AND
/// every hit fulfilled every condition — every tick of scout-service would be
/// set, and "which skill is missing" would have no answer (ADR-0036 decision
/// 2). The first condition would then be empty too: there would be nothing to
/// sort by. The information the scout is to give begins here.</para>
///
/// <para>The search runs exclusively over <em>named</em> skills (ADR-0033):
/// over what a person typed into their profile themselves. A piece of evidence
/// — a GitHub topic, a technology at a station — is a statement about an
/// artefact, and whoever searched by it would silently turn it into one about
/// the human being. profile-service does not know evidence at all, and this
/// query is the reason to leave it that way.</para>
/// </remarks>
public sealed record InternalProfileSearchQuery(
    int PageLength,
    string? Cursor,
    IReadOnlyList<string>? Skills = null,
    string Location = "",
    bool RemoteOnly = false) : IQueryMessage<ProfilePage>;

/// <summary>Passes the page through, in a stable order.</summary>
public sealed class InternalProfileSearchHandler(IProfileStore store)
    : IRequestHandler<InternalProfileSearchQuery, ProfilePage>
{
    /// <summary>Upper limit per page.</summary>
    /// <remarks>
    /// The ledger's batch question carries at most a hundred pairs, so fifty
    /// human beings — whoever handed out more here would build a page whose
    /// release the caller cannot check in one piece.
    /// <para>
    /// The numbers stood at <c>KandidatenHandler</c> until 11.09.2026. It fell
    /// with <c>GET /candidates</c>; the limits stayed, because the reason for
    /// them stayed.
    /// </para>
    /// </remarks>
    public const int MaxPageLength = 50;

    /// <summary>What a page carries when nobody says otherwise.</summary>
    public const int DefaultPageLength = 20;

    /// <summary>How many skills may be filtered by.</summary>
    /// <remarks>
    /// The same consideration as with the page cap: without a limit a caller
    /// builds an arbitrarily expensive query with a single address line.
    /// <para>
    /// Twenty, the most an advert may name (<c>SkillList.MaxEntries</c> in
    /// jobs-service): the scout per advert hands all of them. At ten the
    /// door CUT them silently, and the search found people for half an advert
    /// while the ticks pretended to cover all of it (VISION-FRAGEN B1).
    /// </para>
    /// </remarks>
    public const int MaxSkillFilters = 20;

    /// <inheritdoc />
    public Task<ProfilePage> Handle(
        InternalProfileSearchQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageLength = Math.Clamp(
            request.PageLength <= 0 ? DefaultPageLength : request.PageLength, 1, MaxPageLength);

        var skills = (request.Skills ?? [])
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .Take(MaxSkillFilters)
            .ToArray();

        return store.PageAsync(
            new PageRequest(
                pageLength, PageCursor.Read(request.Cursor), skills,
                request.Location, request.RemoteOnly),
            cancellationToken);
    }
}
