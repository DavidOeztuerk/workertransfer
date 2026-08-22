using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>What a company gets when it asks to read a résumé.</summary>
/// <remarks>
/// Two cases only, and the second one covers both "there is none" and "it is
/// not released to you". They must stay indistinguishable: a status code that
/// told them apart would be an oracle over every guessed id (ADR-0020 §1).
/// </remarks>
public abstract record Lebenslaufergebnis
{
    private Lebenslaufergebnis()
    {
    }

    /// <summary>Here it is.</summary>
    public sealed record Gefunden(Lebenslauf Lebenslauf) : Lebenslaufergebnis;

    /// <summary>Hidden or non-existent — from outside the same thing.</summary>
    public sealed record NichtSichtbar : Lebenslaufergebnis;
}

/// <summary>A company reads one person's résumé.</summary>
public sealed record SichtbarerLebenslaufAbfrage(SubjectId Wer, TenantId Firma)
    : IAbfrage<Lebenslaufergebnis>;

/// <inheritdoc cref="SichtbarerLebenslaufAbfrage" />
public sealed class SichtbarerLebenslaufHandler(
    ILebenslaufSpeicher speicher,
    IEinwilligungstor tor) : IRequestHandler<SichtbarerLebenslaufAbfrage, Lebenslaufergebnis>
{
    /// <inheritdoc />
    public async Task<Lebenslaufergebnis> Handle(
        SichtbarerLebenslaufAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lebenslauf = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (lebenslauf is null)
        {
            // No ledger call for a résumé that does not exist: a needless round
            // trip, and it would report guessed subject ids to the ledger.
            return new Lebenslaufergebnis.NichtSichtbar();
        }

        // EinwilligungSchweigt travels on deliberately: the endpoint turns it
        // into 503, which is neither "there is nothing here" nor showing it.
        return await tor.DarfLebenslaufLesenAsync(request.Wer, request.Firma, cancellationToken)
            ? new Lebenslaufergebnis.Gefunden(lebenslauf)
            : new Lebenslaufergebnis.NichtSichtbar();
    }
}
