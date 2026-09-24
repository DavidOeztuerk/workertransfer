namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>The caller's access token, for the way to the ledger.</summary>
/// <remarks>
/// The ledger is written <em>on behalf of the person</em> and not with a
/// service account: she grants the release, not this service, and the ledger
/// turns a foreign actor away. So the token has to travel one hop further.
/// <para>
/// A port instead of a <c>bearer</c> field on every command — which is how the
/// Python service did it. A token is a transport detail: as a command field it
/// has to be filled at every call site, stands in every test fixture, and can be
/// filled with somebody else's.
/// </para>
/// </remarks>
public interface ICallerToken
{
    /// <summary><c>null</c> when the request carried none.</summary>
    string? Value { get; }
}
