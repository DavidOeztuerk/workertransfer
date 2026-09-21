namespace WorkerTransfer.Portfolio.Application.Ports;

/// <summary>Die Kennung, die eine ResumeRequest über Dienstgrenzen hinweg zusammenhält.</summary>
public interface IKorrelation
{
    /// <summary><c>null</c> außerhalb einer ResumeRequest.</summary>
    string? Aktuell { get; }
}
