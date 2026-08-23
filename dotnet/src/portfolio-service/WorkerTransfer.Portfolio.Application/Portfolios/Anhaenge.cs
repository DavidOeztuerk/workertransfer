using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Nachrichten;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Domain.Ablage;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Portfolios;

/// <summary>Legt eine Arbeitsprobe ab.</summary>
/// <param name="Dateiname">Wie die Datei beim Absender hieß — nur als Hinweis.</param>
public sealed record AnhangAblegenBefehl(
    SubjectId Wer, string Dateiname, string Medientyp, Stream Inhalt) : IBefehl<string>;

/// <inheritdoc cref="AnhangAblegenBefehl" />
/// <remarks>
/// Der Name, unter dem die Datei liegt, wird von der Ablage vergeben und
/// zurückgegeben. Der Client schickt ihn danach beim Speichern des Portfolios
/// mit — er benennt also nie, wohin geschrieben wird.
/// </remarks>
public sealed class AnhangAblegenHandler(IAblage ablage)
    : IRequestHandler<AnhangAblegenBefehl, string>
{
    /// <inheritdoc />
    public Task<string> Handle(AnhangAblegenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ablage.LegeAbAsync(
            request.Wer, request.Dateiname, request.Medientyp, request.Inhalt, cancellationToken);
    }
}

/// <summary>Holt eine Arbeitsprobe.</summary>
public sealed record AnhangAbfrage(SubjectId Wer, string Name, SubjectId Aufrufer)
    : IAbfrage<Abgelegtes?>;

/// <summary>Fragt dasselbe Tor wie das Portfolio selbst.</summary>
/// <remarks>
/// <b>Dieselbe Einwilligung, kein zweites Tor</b> (ADR-0021). Und zusätzlich
/// die Frage, ob ein Eintrag den Anhang überhaupt nennt: sonst bliebe eine aus
/// dem Portfolio entfernte Arbeitsprobe über ihren Namen erreichbar.
/// <para>
/// Das eigene liest man ohne Ledgerfrage — sonst könnte man seine eigene
/// Arbeitsprobe nicht mehr ansehen, nachdem man die Freigabe zurückgenommen
/// hat.
/// </para>
/// </remarks>
public sealed class AnhangHandler(
    IPortfoliospeicher speicher, IAblage ablage, IEinwilligungstor tor)
    : IRequestHandler<AnhangAbfrage, Abgelegtes?>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<Abgelegtes?> Handle(
        AnhangAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selbst = request.Wer == request.Aufrufer;

        if (!selbst && !await tor.DarfSehenAsync(request.Wer, cancellationToken))
        {
            return null;
        }

        var portfolio = await speicher.HoleAsync(request.Wer, cancellationToken);

        return portfolio?.Nennt(request.Name) == true
            ? await ablage.HoleAsync(request.Wer, request.Name, cancellationToken)
            : null;
    }
}
