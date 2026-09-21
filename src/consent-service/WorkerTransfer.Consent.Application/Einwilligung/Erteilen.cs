using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>Give a permission.</summary>
/// <param name="Handelnder">Who is asking. Must be the subject.</param>
/// <param name="Firma">The company they are acting for, for the trail only.</param>
/// <param name="Gegenstand">Whose consent it is, as the caller named it.</param>
/// <param name="Faehigkeit">Which permission.</param>
/// <param name="Grund">Optional — giving a permission needs no justification.</param>
/// <remarks>
/// A command, not a <c>PUT</c> on a resource: it appends a fact. Mapping it
/// onto a verb that implies mutation would misrepresent an append-only ledger.
/// </remarks>
public sealed record GrantConsentCommand(
    SubjectId Handelnder,
    TenantId? Firma,
    Guid Gegenstand,
    string Faehigkeit,
    string? Grund) : IBefehl<ConsentResult>;

/// <summary>Records the grant.</summary>
public sealed class GrantConsentHandler(ConsentWriter schreiber)
    : IRequestHandler<GrantConsentCommand, ConsentResult>
{
    /// <inheritdoc />
    public Task<ConsentResult> Handle(
        GrantConsentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return schreiber.GrantAsync(
            request.Handelnder, request.Firma, request.Gegenstand, request.Faehigkeit,
            request.Grund, cancellationToken);
    }
}
