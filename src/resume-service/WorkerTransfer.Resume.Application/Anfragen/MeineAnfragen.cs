using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>One request as the person sees it — the proceeding and what holds.</summary>
/// <param name="Anfrage">What happened.</param>
/// <param name="Aktiv">
/// What holds, freshly from the ledger.
/// </param>
/// <remarks>
/// The two can disagree, and that is the point: after a withdrawal
/// <see cref="Anfragestand.Granted"/> stays and <paramref name="Aktiv"/> falls
/// to <c>false</c>. Exactly why the permission is not a field on the
/// proceeding.
/// </remarks>
public sealed record Anfrageansicht(Anfrage Anfrage, bool Aktiv);

/// <summary>Who asked about me, and what they hold right now.</summary>
public sealed record MeineAnfragenAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Anfrageansicht>>;

/// <summary>Reads the proceedings, then asks the ledger about the granted ones.</summary>
/// <remarks>
/// Only for granted ones: for pending and declined the answer is settled, and
/// every question costs a round trip. The granted ones go out as one batch —
/// the Python service made one call per row and paid for a person with forty
/// requests in seconds (ADR-0030).
/// </remarks>
public sealed class MeineAnfragenHandler(IAnfragenSpeicher speicher, IEinwilligungstor tor)
    : IRequestHandler<MeineAnfragenAbfrage, IReadOnlyList<Anfrageansicht>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Anfrageansicht>> Handle(
        MeineAnfragenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anfragen = await speicher.FuerPersonAsync(request.Wer, cancellationToken);
        var erteilte = anfragen.Where(a => a.Stand == Anfragestand.Granted).ToList();

        var urteile = erteilte.Count == 0
            ? []
            : await tor.DuerfenLebenslaufLesenAsync(
                [.. erteilte.Select(a => (a.Wer, a.Firma))], cancellationToken);

        var aktivNach = new Dictionary<Guid, bool>();

        for (var i = 0; i < erteilte.Count; i++)
        {
            aktivNach[erteilte[i].Id] = urteile[i];
        }

        return [.. anfragen.Select(a => new Anfrageansicht(
            a, aktivNach.TryGetValue(a.Id, out var aktiv) && aktiv))];
    }
}
