namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>Das Zugriffstoken des Aufrufers, für den Weg zum Ledger.</summary>
/// <remarks>
/// Der Ledger wird im Auftrag des Aufrufers gefragt und nicht mit einem
/// Dienstkonto, damit sein Protokoll festhält, wer wirklich gefragt hat.
/// </remarks>
public interface IAufrufertoken
{
    /// <summary><c>null</c>, wenn die Anfrage keines trug.</summary>
    string? Wert { get; }
}
