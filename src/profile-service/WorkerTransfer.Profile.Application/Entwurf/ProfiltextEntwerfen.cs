using Noelia.Core.Identity;
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
public sealed record DraftQuery(SubjectId Wer, string Wunsch) : IAbfrage<string>;

/// <summary>Baut den Prompt aus dem GESPEICHERTEN PersonProfile und fragt den Anbieter.</summary>
/// <remarks>
/// Aus dem gespeicherten PersonProfile, nicht aus der ResumeRequest: was der Aufrufer nicht
/// schicken kann, kann er nicht in einen fremden Dienst schleusen. Der Wunsch
/// ist die einzige Zeile, die er beisteuert, und sie ist an ihn selbst
/// gerichtet.
/// <para>
/// Nichts wird gespeichert — nicht der Prompt, nicht die Antwort. Kein Eintrag
/// in der Prüfspur: eine Zeile „hat um Hilfe gebeten“ wäre eine Aussage über
/// einen Menschen, die niemand braucht und die ihn überdauert.
/// </para>
/// </remarks>
public sealed class DraftHandler(IProfileStore store, IDrafter entwerfer)
    : IRequestHandler<DraftQuery, string>
{
    /// <inheritdoc />
    /// <exception cref="EntwurfNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    public async Task<string> Handle(DraftQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await store.GetAsync(request.Wer, cancellationToken);

        var lage = new DraftSituation(
            profile?.Ueberschrift ?? string.Empty,
            profile?.Text ?? string.Empty,
            profile?.Faehigkeiten.Werte ?? [],
            request.Wunsch);

        return await entwerfer.DraftAsync(lage, cancellationToken);
    }
}
