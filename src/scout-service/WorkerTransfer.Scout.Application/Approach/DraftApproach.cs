using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Messages;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Application.Approach;

/// <summary>A request for a draft of a first message to a human being.</summary>
/// <param name="Company">The caller's company — from the token.</param>
/// <param name="Subject">Who is to be approached.</param>
/// <param name="Searched">What the company searched for.</param>
/// <param name="Wish">What the human being wants changed about the draft.</param>
/// <remarks>
/// An <see cref="IQueryMessage{TResponse}"/> and expressly no command: nothing
/// is written, so no transaction may open either. The draft lives in the form
/// until a human being sends it — and they send it themselves.
/// </remarks>
public sealed record ApproachQuery(
    TenantId Company,
    SubjectId Subject,
    IReadOnlyList<string> Searched,
    string Wish) : IQueryMessage<string>;

/// <summary>"Nobody home" — there is nothing to draft here for this person.</summary>
/// <remarks>
/// The same answer for "not released" and "does not exist", and it has to stay
/// so: a difference would say whether this human being is on the platform
/// (ADR-0020). The message is the problem document's detail and therefore wire.
/// </remarks>
public sealed class NoContactPerson() : Exception("no such profile");

/// <summary>
/// Asks the ledger first, then builds the context — and stores nothing.
/// </summary>
/// <remarks>
/// <para><strong>The ledger stands in front of the model, not beside
/// it.</strong> Without this order somebody could order a draft about a person
/// who never released anything to them — and the model would get to see their
/// words. Asked synchronously and without a cache, as everywhere
/// (ADR-0013).</para>
///
/// <para><strong>The context comes from the STORED profile</strong>, not from
/// the request: what the caller cannot send, they cannot smuggle into a foreign
/// service. They contribute exactly two things, and both are their own words —
/// what they searched for, and what they want changed about the draft.</para>
///
/// <para><strong>Nothing is stored</strong> — not the prompt, not the answer,
/// and no note "ordered a draft". Such a note would be exactly the register "who
/// was interested in whom" that ADR-0033 does not want to arise.</para>
///
/// <para>And expressly <strong>no notification</strong> to the approached
/// person: they learn from the search that they were discovered
/// (<c>profile_discovered</c>), and a second message "somebody is drafting
/// something to you right now" would be a counter over one's own
/// visibility.</para>
/// </remarks>
public sealed class ApproachHandler(
    IConsentGate gate,
    IProfileSearch profileSearch,
    IDrafter drafter) : IRequestHandler<ApproachQuery, string>
{
    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    /// <exception cref="NoContactPerson">Not released, or there is nobody.</exception>
    /// <exception cref="DraftUnavailable">No provider, or it is silent.</exception>
    public async Task<string> Handle(
        ApproachQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verdicts = await gate.MaySeeAllAsync(
            [request.Subject], request.Company, cancellationToken);

        if (verdicts.Count != 1)
        {
            throw new ConsentGateSilent($"{verdicts.Count} answers to one question");
        }

        if (!verdicts[0])
        {
            throw new NoContactPerson();
        }

        var profile = await profileSearch.GetAsync(request.Subject, cancellationToken)
                      ?? throw new NoContactPerson();

        // Only words that are already visible and NAMED. No evidence: it is
        // visible but not named — attributing it to a person is the step ADR-0033
        // reserves for the person themselves.
        var situation = new ApproachSituation(
            profile.Headline,
            profile.Named,
            request.Searched,
            request.Wish);

        return await drafter.DraftAsync(situation, cancellationToken);
    }
}
