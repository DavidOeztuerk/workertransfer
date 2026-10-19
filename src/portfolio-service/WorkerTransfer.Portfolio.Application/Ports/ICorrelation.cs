namespace WorkerTransfer.Portfolio.Application.Ports;

/// <summary>The id that holds one request together across service boundaries.</summary>
public interface ICorrelation
{
    /// <summary><c>null</c> outside a request.</summary>
    string? Current { get; }
}
