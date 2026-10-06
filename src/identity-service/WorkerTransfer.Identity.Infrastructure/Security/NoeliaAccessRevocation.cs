using Noelia.Abstractions.Security;
using Noelia.Core.Identity;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>Writes revocations into Noelia's store — Valkey in the stack, shared by all services.</summary>
/// <param name="writer">The store's write side.</param>
public sealed class NoeliaAccessRevocation(ITokenRevocationWriter writer) : IAccessRevocation
{
    /// <inheritdoc />
    public Task RevokeTokenAsync(
        string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        writer.RevokeTokenAsync(tokenId, expiresAt, "sign-out", cancellationToken);

    /// <inheritdoc />
    public Task RevokeEverythingBeforeAsync(
        SubjectId subject, DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        writer.RevokeSubjectBeforeAsync(subject.Value.ToString(), cutoff, "account erasure", cancellationToken);
}
