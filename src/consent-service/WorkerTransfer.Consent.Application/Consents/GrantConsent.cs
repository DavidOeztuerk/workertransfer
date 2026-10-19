using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Messages;

namespace WorkerTransfer.Consent.Application.Consents;

/// <summary>Give a permission.</summary>
/// <param name="Actor">Who is asking. Must be the subject.</param>
/// <param name="Company">The company they are acting for, for the trail only.</param>
/// <param name="Subject">Whose consent it is, as the caller named it.</param>
/// <param name="Capability">Which permission.</param>
/// <param name="Reason">Optional — giving a permission needs no justification.</param>
/// <remarks>
/// A command, not a <c>PUT</c> on a resource: it appends a fact. Mapping it
/// onto a verb that implies mutation would misrepresent an append-only ledger.
/// </remarks>
public sealed record GrantConsentCommand(
    SubjectId Actor,
    TenantId? Company,
    Guid Subject,
    string Capability,
    string? Reason) : ICommandMessage<ConsentResult>;

/// <summary>Records the grant.</summary>
public sealed class GrantConsentHandler(ConsentWriter writer)
    : IRequestHandler<GrantConsentCommand, ConsentResult>
{
    /// <inheritdoc />
    public Task<ConsentResult> Handle(
        GrantConsentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return writer.GrantAsync(
            request.Actor, request.Company, request.Subject, request.Capability,
            request.Reason, cancellationToken);
    }
}
