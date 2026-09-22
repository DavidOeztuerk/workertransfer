namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>The caller's access token, for the way to the ledger.</summary>
/// <remarks>
/// The ledger is asked <em>on behalf of the caller</em> and not with a service
/// account, so its log records who really asked. A port rather than a
/// <c>bearer</c> field on every command: a token is a transport detail that
/// would otherwise have to be filled at every call site — and could be filled
/// with somebody else's.
/// </remarks>
public interface ICallerToken
{
    /// <summary><c>null</c> when the request carried none.</summary>
    string? Value { get; }
}
