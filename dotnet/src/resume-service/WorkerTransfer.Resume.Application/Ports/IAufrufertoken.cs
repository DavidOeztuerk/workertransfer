namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>The caller's own access token, for the call onward to the ledger.</summary>
/// <remarks>
/// The ledger is asked <em>on behalf of the caller</em> and not with a service
/// account, so its own log records who really asked. That means the token has
/// to travel one hop further.
/// <para>
/// A port instead of a <c>bearer</c> field on every command — which is how the
/// Python service did it. A token is a transport detail: on a command it is a
/// field that has to be filled at every call site, that shows up in every test
/// fixture, and that can be filled with somebody else's. Here exactly one
/// implementation reads it out of the request that is running.
/// </para>
/// </remarks>
public interface IAufrufertoken
{
    /// <summary><c>null</c> when the request carried none.</summary>
    string? Wert { get; }
}
