namespace WorkerTransfer.Transfer.Api.Berechtigung;

/// <summary>Die Rechte, die dieser Dienst kennt.</summary>
/// <remarks>
/// <para><strong>Die Linie liegt am Geld, nicht am Gespräch.</strong> Interesse
/// zeigen, jemanden nach dem Marktstatus fragen, die eigenen Vorgänge lesen —
/// das ist Anbahnung und die Arbeit, für die jemand eingeladen wird. Ein
/// <see cref="Anbieten">Angebot</see> nennt Eintrittstermin und Vermittlungs-
/// gebühr, und <see cref="Abschliessen">abschliessen</see> stellt fest, dass
/// beides gilt. Beides bindet das Unternehmen.</para>
///
/// <para><strong><c>withdraw</c> steht bewusst NICHT hier.</strong> Wer einen
/// Vorgang anfangen darf, muss ihn beenden dürfen — sonst ist eine Einladung
/// eine Falle: man kommt hinein und nicht wieder heraus. Denselben Satz trägt
/// schon <c>/decline</c> für die Person.</para>
/// </remarks>
public static class Vorgangsrechte
{
    /// <summary>Ein Angebot machen — Termin und Gebühr.</summary>
    public const string Anbieten = "transfer.offer";

    /// <summary>Einen Vorgang abschliessen.</summary>
    public const string Abschliessen = "transfer.complete";
}
