using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>Every event of one's own history, oldest first.</summary>
/// <param name="Wer">Taken from the token, never from the request.</param>
/// <remarks>
/// <c>/consent/me</c> deliberately shows only what holds; a history reveals who
/// once asked, and that is more than an overview page promises. Here it is
/// exactly right — this is the person's own record, going to the person.
/// <para>
/// And here the withdrawal reason travels with it. It is free text they wrote
/// about themselves; towards them there is no reason to hold it back. That is
/// the difference between "belongs to them" and "concerns others".
/// </para>
/// </remarks>
public sealed record MeineGeschichteAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<ConsentEvent>>;

/// <summary>Reads the whole stream, unreduced.</summary>
public sealed class MeineGeschichteHandler(IConsentLedger buch)
    : IRequestHandler<MeineGeschichteAbfrage, IReadOnlyList<ConsentEvent>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ConsentEvent>> Handle(
        MeineGeschichteAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return buch.VerlaufAsync(request.Wer, cancellationToken);
    }
}
