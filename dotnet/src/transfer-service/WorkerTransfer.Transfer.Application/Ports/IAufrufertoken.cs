namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>Das Zugriffstoken des Aufrufers, für den Weg zum Ledger.</summary>
/// <remarks>
/// Der Ledger wird <em>im Auftrag des Aufrufers</em> gefragt und nicht mit
/// einem Dienstkonto, damit sein Protokoll festhält, wer wirklich gefragt hat.
/// Ein Port statt eines <c>bearer</c>-Feldes an jedem Befehl: ein Token ist ein
/// Transportdetail, das sonst an jeder Aufrufstelle gefüllt werden müsste — und
/// mit dem eines anderen gefüllt werden könnte.
/// </remarks>
public interface IAufrufertoken
{
    /// <summary><c>null</c>, wenn die Anfrage keines trug.</summary>
    string? Wert { get; }
}
