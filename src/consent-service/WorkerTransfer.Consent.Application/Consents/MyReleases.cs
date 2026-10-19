using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Messages;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Consents;

/// <summary>What currently holds — for the person themselves, and only them.</summary>
/// <param name="Subject">Taken from the token, never from the request.</param>
/// <remarks>
/// There is no subject parameter, in the path or in the query. Somebody else's
/// list would say which <em>other</em> companies hold access — a statement
/// about a person that nobody but they may make. What you cannot name, you
/// cannot forge: the same rule as the tenant claim (ADR-0018) and the company
/// domain (ADR-0019).
/// <para>
/// Unlike <c>/check</c>, which is open to any authenticated caller about
/// anyone: there the answer is a single yes or no to a question the caller had
/// already formed. Here it would be an overview.
/// </para>
/// </remarks>
public sealed record MyReleasesQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<Release>>;

/// <summary>One permission that holds right now.</summary>
/// <param name="Capability">Which permission.</param>
/// <param name="Since">
/// When it took effect — the effective grant, not the first one. Somebody who
/// withdrew and later granted again has held it since the second time.
/// </param>
public sealed record Release(Capability Capability, DateTimeOffset Since);

/// <summary>Reduces every capability of one person and keeps what holds.</summary>
public sealed class MyReleasesHandler(IConsentLedger ledger)
    : IRequestHandler<MyReleasesQuery, IReadOnlyList<Release>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Release>> Handle(
        MyReleasesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var latest = await ledger.LatestPerSkillAsync(request.Subject, cancellationToken);

        // The same reduction as /check, applied to a stream of length one. This
        // list must not become a second reading of what "holds" means.
        //
        // Withdrawn and erased capabilities drop out: the page answers "what
        // holds", not "what was". A history says who once asked, and that is
        // more than an overview promises.
        return [.. latest
            .Where(consentEvent => Projection.State([consentEvent]).Granted)
            .Select(consentEvent => new Release(consentEvent.Capability, consentEvent.RecordedAt))
            .OrderBy(release => release.Capability.Value, StringComparer.Ordinal)];
    }
}
