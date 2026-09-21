using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>Take a permission back.</summary>
/// <param name="Actor">Who is asking. Must be the subject.</param>
/// <param name="Company">The company they are acting for, for the trail only.</param>
/// <param name="Subject">Whose consent it is, as the caller named it.</param>
/// <param name="Capability">Which permission.</param>
/// <param name="Reason">
/// Mandatory. Withdrawing must always be explainable — and the text belongs to
/// the person: <c>/check</c> never carries it, and an erasure clears it.
/// </param>
public sealed record RevokeConsentCommand(
    SubjectId Actor,
    TenantId? Company,
    Guid Subject,
    string Capability,
    string Reason) : ICommand<ConsentResult>;

/// <summary>Records the withdrawal.</summary>
public sealed class RevokeConsentHandler(ConsentWriter writer)
    : IRequestHandler<RevokeConsentCommand, ConsentResult>
{
    /// <inheritdoc />
    public Task<ConsentResult> Handle(
        RevokeConsentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return writer.RevokeAsync(
            request.Actor, request.Company, request.Subject, request.Capability,
            request.Reason, cancellationToken);
    }
}
