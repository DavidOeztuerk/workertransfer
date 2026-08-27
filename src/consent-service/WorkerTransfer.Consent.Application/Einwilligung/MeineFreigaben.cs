using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>What currently holds — for the person themselves, and only them.</summary>
/// <param name="Wer">Taken from the token, never from the request.</param>
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
public sealed record MeineFreigabenAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Freigabe>>;

/// <summary>One permission that holds right now.</summary>
/// <param name="Faehigkeit">Which permission.</param>
/// <param name="Seit">
/// When it took effect — the effective grant, not the first one. Somebody who
/// withdrew and later granted again has held it since the second time.
/// </param>
public sealed record Freigabe(Capability Faehigkeit, DateTimeOffset Seit);

/// <summary>Reduces every capability of one person and keeps what holds.</summary>
public sealed class MeineFreigabenHandler(IConsentLedger buch)
    : IRequestHandler<MeineFreigabenAbfrage, IReadOnlyList<Freigabe>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Freigabe>> Handle(
        MeineFreigabenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var neueste = await buch.NeuesteJeFaehigkeitAsync(request.Wer, cancellationToken);

        // The same reduction as /check, applied to a stream of length one. This
        // list must not become a second reading of what "holds" means.
        //
        // Withdrawn and erased capabilities drop out: the page answers "what
        // holds", not "what was". A history says who once asked, and that is
        // more than an overview promises.
        return [.. neueste
            .Where(ereignis => Projektion.Stand([ereignis]).Granted)
            .Select(ereignis => new Freigabe(ereignis.Capability, ereignis.RecordedAt))
            .OrderBy(freigabe => freigabe.Faehigkeit.Value, StringComparer.Ordinal)];
    }
}
