using Noelia.Core.Identity;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>What a page is narrowed by.</summary>
/// <param name="PageLength">How many rows at most.</param>
/// <param name="After">Where it continues, or <c>null</c> for the beginning.</param>
/// <param name="Skills">The words searched for, canonical.</param>
/// <param name="Location">Part of the text, regardless of upper and lower case.</param>
/// <param name="RemoteOnly">Only whoever expressly ticked remote.</param>
/// <remarks>
/// <para><strong>The words are an OR, and there is no AND any more.</strong>
/// <c>GET /candidates</c> searched with AND and fell on 11.09.2026; the choice
/// between the two fell with it. A branch without a caller is no reserve but
/// what later gets revived wrongly.</para>
///
/// <para>The OR is no loosening either but the precondition for the scout's
/// tick list: under AND every hit fulfils every condition, every tick would be
/// set, and "which skill is missing" would have no answer (ADR-0036 decision
/// 2). The first condition would then be empty too — there would be nothing to
/// sort by.</para>
///
/// <para>Nothing becomes visible through it that would be hidden without the
/// filter: consent is checked afterwards, not here — the store knows no
/// visibility (ADR-0020).</para>
/// </remarks>
public sealed record PageRequest(
    // `PageLength` and not `Count`, and the reason is the same one for which
    // scout-service names its page length so: `anzahl` stands in the
    // vocabulary that `wt.ki.keine-zahl` searches across domain and contracts
    // (ADR-0022 §1). Here it would be a page length and harmless — but an
    // exemption list per service is the place where the next entry is no
    // longer harmless.
    int PageLength,
    PageCursor? After = null,
    IReadOnlyList<string>? Skills = null,
    string Location = "",
    bool RemoteOnly = false);

/// <summary>A page of profiles.</summary>
/// <param name="Items">The rows, most recently changed first.</param>
/// <param name="Next">Where it continues, or <c>null</c> at the end.</param>
public sealed record ProfilePage(IReadOnlyList<PersonProfile> Items, PageCursor? Next);

/// <summary>Where the profiles lie.</summary>
public interface IProfileStore
{
    /// <summary>A person's profile, or <c>null</c>.</summary>
    Task<PersonProfile?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or changes — one profile per person.</summary>
    /// <remarks>
    /// Must be called. The aggregate comes back detached from
    /// <see cref="GetAsync"/>, so without this call every change stays in
    /// memory: without consequence in a test, a lost write in operation.
    /// </remarks>
    Task StoreAsync(PersonProfile profile, CancellationToken cancellationToken = default);

    /// <summary>One page, according to <see cref="PageRequest"/>.</summary>
    Task<ProfilePage> PageAsync(
        PageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes a person's profile and says what remained.</summary>
    /// <remarks>
    /// Always 0: this service knows no retention case. The return value is
    /// there anyway, because every recipient gives the same receipt — the
    /// origin is to learn what remained instead of guessing it (ADR-0027 §3.4).
    /// <para>
    /// On a row that is already gone this is by nature without effect. That is
    /// no carelessness but the precondition for the delivery being allowed to
    /// be "at least once".
    /// </para>
    /// </remarks>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
