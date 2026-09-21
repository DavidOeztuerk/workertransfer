namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>The caller's access token, for the way to the ledger.</summary>
/// <remarks>
/// The ledger is asked on behalf of the caller and not with a service account,
/// so its log records who really asked.
/// </remarks>
public interface ICallerToken
{
    /// <summary><c>null</c> when the request carried none.</summary>
    string? Value { get; }
}
