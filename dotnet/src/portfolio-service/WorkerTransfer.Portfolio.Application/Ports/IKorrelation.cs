namespace WorkerTransfer.Portfolio.Application.Ports;

/// <summary>Die Kennung, die eine Anfrage über Dienstgrenzen hinweg zusammenhält.</summary>
public interface IKorrelation
{
    /// <summary><c>null</c> außerhalb einer Anfrage.</summary>
    string? Aktuell { get; }
}
