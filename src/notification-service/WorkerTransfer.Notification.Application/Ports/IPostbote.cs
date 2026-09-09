using Girder.Core.Identity;

namespace WorkerTransfer.Notification.Application.Ports;

/// <summary>Bittet identity-service, den einen Satz hinauszuschicken.</summary>
/// <remarks>
/// <strong>Dieser Dienst kennt keine E-Mail-Adresse, und das ist der ganze
/// Entwurf.</strong> Der Python-Dienst hatte die Benachrichtigungen aus genau
/// diesem Grund in identity-service belassen: „ein <c>notifications-service</c>
/// bräuchte die E-Mail-Adresse; sie dorthin zu kopieren oder über einen Lookup
/// <c>subject_id → E-Mail</c> herauszureichen hieße, das empfindlichste Datum
/// des Systems zu vervielfachen — für eine Textmail."
/// <para>
/// Der Einwand stimmt, und er wird nicht dadurch beantwortet, dass man ihn
/// ignoriert, sondern indem <em>die andere Hälfte</em> umzieht. Hier liegt,
/// <em>ob</em> etwas hinausgeht: die vier Schalter und die Drossel. Dort liegt,
/// <em>an wen</em>: die Adresse, die identity-service ohnehin hat. Über die
/// Grenze geht nur eine <c>user_id</c>, die überall sonst auch schon geht.
/// </para>
/// <para>
/// Und die Art geht <strong>nicht</strong> mit. identity-service formuliert den
/// einen festen Satz; ein Aufrufer, der die Art mitschicken könnte, wäre der
/// Anfang von „nur diese eine Zeile noch". Die Regel des Python-Dienstes — der
/// Aufrufer hat auf den Text keinen Zugriff — gilt hier über eine
/// Dienstgrenze hinweg und ist damit stärker, nicht schwächer.
/// </para>
/// </remarks>
public interface IPostbote
{
    /// <summary>„Sag dieser Person, dass es etwas Neues gibt."</summary>
    /// <remarks>
    /// Wirft nicht. Ein Fehlschlag hier darf niemals den Vorgang kippen, der
    /// ihn ausgelöst hat — bei der Einwilligung geht es um Erlaubnis (im
    /// Zweifel nein), hier um Höflichkeit.
    /// </remarks>
    Task SchickeAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
