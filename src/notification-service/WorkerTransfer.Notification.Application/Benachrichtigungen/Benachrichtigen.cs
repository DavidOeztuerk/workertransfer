using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Nachrichten;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;

namespace WorkerTransfer.Notification.Application.Benachrichtigungen;

/// <summary>„Sag dieser Person, dass es etwas Neues gibt."</summary>
public sealed record BenachrichtigenBefehl(SubjectId Wer, Benachrichtigungsart Art)
    : IBefehl<bool>;

/// <summary>Legt den Eintrag an und entscheidet, ob Post hinausgeht.</summary>
/// <remarks>
/// Die beiden Hälften sind bewusst verschieden streng:
/// <list type="bullet">
/// <item>Der <strong>Eintrag im Postfach entsteht immer</strong>. Er liegt
/// hinter der Anmeldung, wo geprüft wird, wer liest; ihn zu drosseln hieße,
/// jemandem zu verschweigen, dass etwas passiert ist.</item>
/// <item>Die <strong>Mail wird gedrosselt und abbestellbar</strong>. Sie landet
/// in einem Postfach, das nicht nur der Person gehören muss.</item>
/// </list>
/// <para>
/// Der Rückgabewert sagt, ob gesendet wurde — für Tests und Protokoll. Der
/// Endpunkt gibt diese Auskunft <strong>nicht</strong> weiter: der Aufrufer
/// soll aus der Antwort nicht ableiten können, ob es die Person gibt oder ob
/// sie Post will.
/// </para>
/// </remarks>
public sealed class BenachrichtigenHandler(
    IWunschspeicher wuensche,
    IEingangsspeicher eingaenge,
    TimeProvider uhr) : IRequestHandler<BenachrichtigenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        BenachrichtigenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jetzt = uhr.GetUtcNow();

        await eingaenge.FuegeHinzuAsync(
            Eingang.Lege_an(request.Wer, request.Art, jetzt), cancellationToken);

        var wunsch = await wuensche.HoleAsync(request.Wer, cancellationToken)
                     ?? Benachrichtigungswunsch.Voreingestellt(request.Wer);

        if (!wunsch.Darf(request.Art, jetzt))
        {
            return false;
        }

        // Die Drossel wird gesetzt, BEVOR gesendet wird, und in derselben
        // Transaktion. Scheitert die Zustellung danach, ist diese Nachricht
        // verloren — das ist die richtige Richtung: lieber eine Mail zu wenig
        // als eine Schleife, die es bei jedem Ereignis erneut versucht.
        wunsch.MerkeGesendet(jetzt);
        await wuensche.SichereAsync(wunsch, cancellationToken);

        return true;
    }
}
