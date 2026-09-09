using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Konto;

/// <summary>Die Bewerbungsanschrift zum Anzeigen — nie an ein Modell.</summary>
public sealed record Anschriftansicht(
    string Zeile1,
    string Zeile2,
    string Postleitzahl,
    string Ort,
    string Land,
    string Telefon);

/// <summary>Die eigene Anschrift lesen.</summary>
public sealed record AnschriftAbfrage(SubjectId Wer) : IAbfrage<Anschriftansicht>;

/// <inheritdoc cref="AnschriftAbfrage" />
public sealed class AnschriftHandler(IAnschriften speicher)
    : IRequestHandler<AnschriftAbfrage, Anschriftansicht>
{
    /// <inheritdoc />
    public async Task<Anschriftansicht> Handle(
        AnschriftAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ansicht(await speicher.HoleAsync(request.Wer, cancellationToken));
    }

    internal static Anschriftansicht Ansicht(Anschrift stand) =>
        new(stand.Zeile1, stand.Zeile2, stand.Postleitzahl, stand.Ort, stand.Land, stand.Telefon);
}

/// <summary>Die eigene Anschrift schreiben.</summary>
public sealed record AnschriftSetzenBefehl(
    SubjectId Wer,
    string? Zeile1,
    string? Zeile2,
    string? Postleitzahl,
    string? Ort,
    string? Land,
    string? Telefon) : IBefehl<Anschriftansicht>;

/// <inheritdoc cref="AnschriftSetzenBefehl" />
public sealed class AnschriftSetzenHandler(IAnschriften speicher)
    : IRequestHandler<AnschriftSetzenBefehl, Anschriftansicht>
{
    /// <inheritdoc />
    public async Task<Anschriftansicht> Handle(
        AnschriftSetzenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stand = await speicher.HoleAsync(request.Wer, cancellationToken);
        stand.Setze(
            request.Zeile1,
            request.Zeile2,
            request.Postleitzahl,
            request.Ort,
            request.Land,
            request.Telefon);
        await speicher.SichereAsync(stand, cancellationToken);
        return AnschriftHandler.Ansicht(stand);
    }
}
