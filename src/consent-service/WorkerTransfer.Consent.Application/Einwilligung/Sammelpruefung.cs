using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>Several "may I?" in one round trip.</summary>
/// <remarks>
/// The same question as <see cref="EinwilligungPruefenAbfrage"/>, <em>n</em>
/// times: same access, same synchronous read, same absence of a cache and the
/// same silence about the withdrawal reason (ADR-0030). What gets cheaper is
/// the number of round trips, not the access.
/// <para>
/// The ceiling lives in the boundary contract, not here — two truths about what
/// one request may carry would be one too many.
/// </para>
/// </remarks>
public sealed record SammelpruefungAbfrage(IReadOnlyList<EinwilligungPruefenAbfrage> Paare)
    : IAbfrage<Sammelergebnis>;

/// <summary>Reads every pair in one query, answers in the order asked.</summary>
public sealed class SammelpruefungHandler(IConsentLedger buch)
    : IRequestHandler<SammelpruefungAbfrage, Sammelergebnis>
{
    /// <inheritdoc />
    public async Task<Sammelergebnis> Handle(
        SammelpruefungAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var paare = new List<(SubjectId Subject, Capability Capability)>(request.Paare.Count);

        foreach (var frage in request.Paare)
        {
            if (!Capability.TryParse(frage.Faehigkeit, out var faehigkeit))
            {
                return new Sammelergebnis.Unbrauchbar("capability");
            }

            paare.Add((new SubjectId(frage.Gegenstand), faehigkeit));
        }

        var neueste = await buch.NeuesteAsync(paare, cancellationToken);

        // In the order of the questions, one per pair. The caller maps the
        // answers onto its rows; in any other order it would have to match on
        // (subject, capability), and a pair asked twice would be ambiguous.
        var staende = new List<ConsentState>(paare.Count);

        foreach (var (subject, faehigkeit) in paare)
        {
            staende.Add(neueste.TryGetValue((subject.Value, faehigkeit.Value), out var ereignis)
                ? Projektion.Stand([ereignis])
                : Projektion.Stand([]));
        }

        return new Sammelergebnis.Staende(staende);
    }
}
