using Noelia.Core.Identity;
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
/// <item>Der <strong>Eintrag im Inbox entsteht immer</strong>. Er liegt
/// hinter der Anmeldung, wo geprüft wird, wer liest; ihn zu drosseln hieße,
/// jemandem zu verschweigen, dass etwas passiert ist. <em>Eine</em> Art bildet
/// die Ausnahme, und aus dem umgekehrten Grund: bei
/// <c>profile_discovered</c> wäre ein Eintrag je Hit selbst die Auskunft,
/// die niemand haben soll — ein Zähler über die eigene Sichtbarkeit
/// (ADR-0033). Dort gilt eine Tageskappe, und sie nimmt den Eintrag mit.</item>
/// <item>Die <strong>Mail wird gedrosselt und abbestellbar</strong>. Sie landet
/// in einem Inbox, das nicht nur der Person gehören muss.</item>
/// </list>
/// <para>
/// Der Rückgabewert sagt, ob gesendet wurde — für Tests und Protokoll. Der
/// Endpunkt gibt diese Auskunft <strong>nicht</strong> weiter: der Aufrufer
/// soll aus der Antwort nicht ableiten können, ob es die Person gibt oder ob
/// sie Post will.
/// </para>
/// </remarks>
public sealed class BenachrichtigenHandler(
    IPreferenceStore preferences,
    IInboxStore eingaenge,
    TimeProvider uhr) : IRequestHandler<BenachrichtigenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        BenachrichtigenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = uhr.GetUtcNow();

        // DIE EIGENE KAPPE JE ART, und sie trifft den EINTRAG, nicht nur die
        // Mail. Fuer `profile_discovered` waere ein Eintrag je Hit ein
        // Zaehler ueber die eigene Sichtbarkeit — auf Umwegen genau das
        // Verzeichnis, das ADR-0033 nicht entstehen laesst. Deshalb faellt hier
        // beides weg, und zwar still: der Aufrufer erfaehrt nichts darueber.
        if (Benachrichtigungsarten.Tageskappe(request.Art) is { } kappe
            && await eingaenge.ExistedSinceAsync(
                request.Wer, request.Art, now - kappe, cancellationToken))
        {
            return false;
        }

        await eingaenge.AppendAsync(
            Eingang.Lege_an(request.Wer, request.Art, now), cancellationToken);

        var preference = await preferences.GetAsync(request.Wer, cancellationToken)
                     ?? NotificationPreference.Voreingestellt(request.Wer);

        if (!preference.Darf(request.Art, now))
        {
            return false;
        }

        // Die Drossel wird gesetzt, BEVOR gesendet wird, und in derselben
        // Transaktion. Scheitert die Zustellung danach, ist diese Nachricht
        // verloren — das ist die richtige Richtung: lieber eine Mail zu wenig
        // als eine Schleife, die es bei jedem Ereignis erneut versucht.
        preference.MerkeGesendet(now);
        await preferences.SaveAsync(preference, cancellationToken);

        return true;
    }
}
