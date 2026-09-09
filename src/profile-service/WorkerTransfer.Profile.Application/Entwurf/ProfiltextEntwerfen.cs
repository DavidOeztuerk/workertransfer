using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Entwurf;

/// <summary>Bitte um einen Entwurf für den eigenen Profiltext.</summary>
/// <param name="Wer">Die Person aus dem Token.</param>
/// <param name="Wunsch">Was sie anders haben will.</param>
/// <remarks>
/// Eine <see cref="IAbfrage{TAntwort}"/> und ausdrücklich kein Befehl: es wird
/// nichts geschrieben, also darf auch keine Transaktion aufgehen. Der Entwurf
/// lebt im Formular, bis die Person ihn speichert — und dann ist es ihr Text.
/// </remarks>
public sealed record EntwurfAbfrage(SubjectId Wer, string Wunsch) : IAbfrage<string>;

/// <summary>Baut den Prompt aus dem GESPEICHERTEN Profil und fragt den Anbieter.</summary>
/// <remarks>
/// Aus dem gespeicherten Profil, nicht aus der Anfrage: was der Aufrufer nicht
/// schicken kann, kann er nicht in einen fremden Dienst schleusen. Der Wunsch
/// ist die einzige Zeile, die er beisteuert, und sie ist an ihn selbst
/// gerichtet.
/// <para>
/// Nichts wird gespeichert — nicht der Prompt, nicht die Antwort. Kein Eintrag
/// in der Prüfspur: eine Zeile „hat um Hilfe gebeten“ wäre eine Aussage über
/// einen Menschen, die niemand braucht und die ihn überdauert.
/// </para>
/// </remarks>
public sealed class EntwurfHandler(IProfilspeicher speicher, IEntwerfer entwerfer)
    : IRequestHandler<EntwurfAbfrage, string>
{
    /// <inheritdoc />
    /// <exception cref="EntwurfNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    public async Task<string> Handle(EntwurfAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profil = await speicher.HoleAsync(request.Wer, cancellationToken);

        var lage = new Entwurfslage(
            profil?.Ueberschrift ?? string.Empty,
            profil?.Text ?? string.Empty,
            profil?.Faehigkeiten.Werte ?? [],
            request.Wunsch);

        return await entwerfer.EntwirfAsync(lage, cancellationToken);
    }
}
