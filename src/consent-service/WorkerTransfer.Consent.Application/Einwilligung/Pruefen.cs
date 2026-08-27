using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>May this capability be exercised for this person?</summary>
/// <remarks>
/// Any authenticated caller may ask about any subject. That is what makes the
/// ledger usable as an enabler: a consuming service has to be able to find out
/// whether it may act on somebody's data. The answer reveals whether a
/// capability is granted, never the data behind it — and never the reason a
/// withdrawal was given.
/// <para>
/// Read live, every time. Not an <c>ICacheableQuery</c>, and there is no cache
/// anywhere in this path: a withdrawal has to take effect on the very next
/// read, so a cache here is not a performance detail but a broken promise
/// (ADR-0013).
/// </para>
/// </remarks>
public sealed record EinwilligungPruefenAbfrage(Guid Gegenstand, string Faehigkeit)
    : IAbfrage<Pruefergebnis>;

/// <summary>Reads the newest fact and reduces it.</summary>
public sealed class EinwilligungPruefenHandler(IConsentLedger buch)
    : IRequestHandler<EinwilligungPruefenAbfrage, Pruefergebnis>
{
    /// <inheritdoc />
    public async Task<Pruefergebnis> Handle(
        EinwilligungPruefenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Capability.TryParse(request.Faehigkeit, out var faehigkeit))
        {
            return new Pruefergebnis.Unbrauchbar("capability");
        }

        var neuestes = await buch.NeuestesAsync(
            new SubjectId(request.Gegenstand), faehigkeit, cancellationToken);

        return new Pruefergebnis.Stand(Projektion.Stand(neuestes is null ? [] : [neuestes]));
    }
}
