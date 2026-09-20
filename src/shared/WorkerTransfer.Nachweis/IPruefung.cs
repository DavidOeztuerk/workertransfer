namespace WorkerTransfer.Nachweis;

/// <summary>Eine Frage, die dieser Dienst über sich selbst beantworten kann.</summary>
/// <remarks>
/// <para><strong>Zur Laufzeit und nicht zur Bauzeit.</strong> Das ist der ganze
/// Grund, warum es diese Schnittstelle neben den Testreihen gibt.
/// <c>Adr0022Tests</c> und <c>EntwurfsgrenzeTests</c> prüfen die Feldmengen des
/// Baumes, und sie tun das gut — aber sie sehen nicht, wohin dieser Behälter
/// heute Abend tatsächlich spricht. Der KI-Zugang steht in
/// <c>KiZugangV1</c>, also pro Person in einer Tabelle, und die Ziele der
/// Egress-Grenze stehen in der Umgebung. Kein Test der Welt liest das.</para>
///
/// <para><strong>Eine Prüfung stellt eine Frage, die ein Programm beantworten
/// kann.</strong> Ob ein AV-Vertrag angemessen ist, ob eine Risikobewertung
/// etwas taugt, ob im Prompt personenbezogene Daten stehen, ob eine Nutzung
/// nach Anhang III hochriskant ist — nichts davon. Wer so etwas prüfen will,
/// hat keine Prüfung, sondern eine Frage an einen Menschen, und die gehört in
/// <see cref="Rechtsbezug.Leser"/>.</para>
///
/// <para><strong>Sie wirft nicht.</strong> Sie darf es — <c>Nachweislauf</c>
/// fängt und setzt einen festen Satz ein —, aber sie soll es nicht: eine
/// Ausnahme trägt bei vielen Anbietern den Endpunkt und manchmal die Nutzlast,
/// und der Lauf kann dann nur noch verschweigen statt berichten.</para>
/// </remarks>
public interface IPruefung
{
    /// <summary>Die Kennung, punktiert und stabil — etwa <c>wt.ki.anbieter</c>.</summary>
    /// <remarks>
    /// Stabil, weil sie in einem Ticket landet und in einem unterschriebenen
    /// Dokument steht. Sie umzubenennen heißt, ein Dokument von gestern
    /// unlesbar zu machen.
    /// </remarks>
    string Id { get; }

    /// <summary>Auf welche Seite der Befund gehört.</summary>
    Bereich Bereich { get; }

    /// <summary>Wonach die Regelwerke fragen, die diesen Befund gebrauchen können.</summary>
    /// <remarks>
    /// Leer ist erlaubt und heißt: diese Prüfung ist Betriebsauskunft und
    /// belegt keine Pflicht. Ein <em>einzelner</em> Bezug mit leerem
    /// <see cref="Rechtsbezug.Leser"/> ist dagegen nie erlaubt.
    /// </remarks>
    IReadOnlyList<Rechtsbezug> Bezuege => [];

    /// <summary>Sieht nach und berichtet.</summary>
    /// <param name="ct">Bricht ab, wenn der Aufrufer auflegt.</param>
    /// <returns>Was vorgefunden wurde.</returns>
    Task<Befund> LaufenAsync(CancellationToken ct = default);
}
