using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>Take a permission back.</summary>
/// <param name="Handelnder">Who is asking. Must be the subject.</param>
/// <param name="Firma">The company they are acting for, for the trail only.</param>
/// <param name="Gegenstand">Whose consent it is, as the caller named it.</param>
/// <param name="Faehigkeit">Which permission.</param>
/// <param name="Grund">
/// Mandatory. Withdrawing must always be explainable — and the text belongs to
/// the person: <c>/check</c> never carries it, and an erasure clears it.
/// </param>
public sealed record RevokeConsentCommand(
    SubjectId Handelnder,
    TenantId? Firma,
    Guid Gegenstand,
    string Faehigkeit,
    string Grund) : IBefehl<ConsentResult>;

/// <summary>Records the withdrawal.</summary>
public sealed class RevokeConsentHandler(ConsentWriter schreiber)
    : IRequestHandler<RevokeConsentCommand, ConsentResult>
{
    /// <inheritdoc />
    public Task<ConsentResult> Handle(
        RevokeConsentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return schreiber.RevokeAsync(
            request.Handelnder, request.Firma, request.Gegenstand, request.Faehigkeit,
            request.Grund, cancellationToken);
    }
}
