namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Das Zugriffstoken des Aufrufers, für den Weg zum Ledger.</summary>
/// <remarks>
/// Der Ledger wird <em>im Auftrag der Person</em> geschrieben und nicht mit
/// einem Dienstkonto: sie erteilt die Freigabe, nicht dieser Dienst, und der
/// Ledger weist einen fremden Akteur ab. Also muss das Token einen Sprung
/// weiter reisen.
/// <para>
/// Ein Port statt eines <c>bearer</c>-Feldes an jedem Befehl — so hatte es der
/// Python-Dienst. Ein Token ist ein Transportdetail: als Befehlsfeld muss es an
/// jeder Aufrufstelle gefüllt werden, steht in jeder Testvorrichtung, und lässt
/// sich mit dem eines anderen füllen.
/// </para>
/// </remarks>
public interface IAufrufertoken
{
    /// <summary><c>null</c>, wenn die Anfrage keines trug.</summary>
    string? Wert { get; }
}
