namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>identity-service (internal endpoint) did not answer.</summary>
/// <remarks>
/// Thrown when the internal wire to identity-service is unreachable, the secret
/// does not match or the answer is unusable. The endpoint filters translate it
/// into a 503, so the caller learns that this service cannot answer right now —
/// not that the company does not exist.
/// </remarks>
public sealed class CompanyLookupSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);
