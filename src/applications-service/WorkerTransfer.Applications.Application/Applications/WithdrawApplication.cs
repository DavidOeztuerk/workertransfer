using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Messages;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Withdrawing — the case stays, the data is gone.</summary>
/// <remarks>
/// That somebody applied and withdrew belongs to the history of the process in
/// the company. The person behind it can no longer be seen afterwards.
/// </remarks>
public sealed record WithdrawApplicationCommand(Guid Id, SubjectId Subject)
    : ICommandMessage<ApplicationResult>;

/// <summary>Sets the state and revokes the releases.</summary>
public sealed class WithdrawApplicationHandler(
    IApplicationStore store,
    IConsentWriter ledger,
    TimeProvider clock) : IRequestHandler<WithdrawApplicationCommand, ApplicationResult>
{
    /// <inheritdoc />
    public async Task<ApplicationResult> Handle(
        WithdrawApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await store.GetAsync(request.Id, cancellationToken);

        // Not there and not mine are the same from outside.
        if (application is null || application.Subject != request.Subject)
        {
            return new ApplicationResult.Unknown();
        }

        try
        {
            application.Withdraw(request.Subject, clock.GetUtcNow());
        }
        catch (TransitionNotAllowed error)
        {
            return new ApplicationResult.StateConflict(error.Message);
        }

        // Revoke unconditionally — also what was perhaps never granted. The
        // ledger tolerates a revocation without a prior grant, and the
        // alternative would be relying on our own row to know what stands in
        // the ledger.
        await ledger.RevokeAsync(
            request.Subject, ConsentKey.All(application.Company), cancellationToken);

        await store.SaveAsync(application, cancellationToken);

        return new ApplicationResult.Done(application);
    }
}
