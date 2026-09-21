using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Pruefspur;

namespace WorkerTransfer.Profile.Application.Erasure;

/// <summary>„Lösche alles, was du über diesen Menschen hältst.“</summary>
/// <param name="Wer">Die Person.</param>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

/// <summary>Was „löschen“ in diesem Dienst heißt (ADR-0027 §2).</summary>
/// <remarks>
/// Die Zeile fällt vollständig: Überschrift, Text, Ort, Fähigkeiten. Es gibt
/// hier nichts, was einem anderen gehört, also auch nichts zu erhalten und
/// nichts zu anonymisieren.
/// <para>
/// Kein Sichtbarkeitsfeld, das man stattdessen umlegen könnte: ob ein PersonProfile
/// gezeigt werden darf, lebt allein im Ledger (ADR-0020). Ein „unsichtbar
/// schalten“ wäre hier also nicht die schonendere Löschung, sondern gar keine.
/// </para>
/// <para>
/// Antwortet mit der Zahl der stehengebliebenen Zeilen — immer 0. Der Wert ist
/// trotzdem da, weil jeder Empfänger dieselbe Quittung gibt: der Ursprung soll
/// erfahren, was blieb, statt es zu vermuten.
/// </para>
/// </remarks>
public sealed class ErasePersonHandler(
    IProfileStore store,
    IAuditTrail spur,
    TimeProvider uhr) : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(
        ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var geblieben = await store.DeleteAsync(request.Wer, cancellationToken);

        // Die Spur hält fest, DASS gelöscht wurde, und nennt keinen Akteur:
        // hier handelt kein Mensch, sondern der Auftrag, den einer vor Stunden
        // erteilt hat. Was gelöscht wurde, steht nirgends — das wäre eine Kopie
        // dessen, was gerade verschwunden ist.
        await spur.AppendOneAsync(
            new AuditEntry(
                AuditAction.ProfileDeleted, uhr.GetUtcNow(), betroffen: request.Wer),
            cancellationToken);

        return geblieben;
    }
}
