using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Messages;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>The company moves the application through the process.</summary>
public sealed record MoveApplicationCommand(Guid Id, TenantId Company, string State)
    : ICommandMessage<ApplicationResult>;

/// <summary>Sets the state and records the notification.</summary>
public sealed class MoveApplicationHandler(
    IApplicationStore store,
    IOutbox outbox,
    TimeProvider clock) : IRequestHandler<MoveApplicationCommand, ApplicationResult>
{
    /// <inheritdoc />
    public async Task<ApplicationResult> Handle(
        MoveApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await store.GetAsync(request.Id, cancellationToken);

        // A foreign application looks like none from outside.
        if (application is null || application.Company != request.Company)
        {
            return new ApplicationResult.Unknown();
        }

        // The contract does not let through a state word that does not exist —
        // but a handler should not crash without it either. The member names
        // ARE the wire words, compared case-insensitively.
        if (!Enum.TryParse<ApplicationState>(request.State, ignoreCase: true, out var wanted)
            || !Enum.IsDefined(wanted))
        {
            return new ApplicationResult.InvalidInput(
                "That is not a status an application can have");
        }

        try
        {
            application.Move(wanted, clock.GetUtcNow());
        }
        catch (TransitionNotAllowed error)
        {
            return new ApplicationResult.StateConflict(error.Message);
        }

        await store.SaveAsync(application, cancellationToken);

        // In the SAME transaction as the move (ADR-0025). Before, an HTTP call
        // stood here after the commit, and its failure was swallowed — the
        // promise "a failed mail must not topple the case" held, but the price
        // was that the message was then gone for ever.
        await outbox.NoteAsync(application.Subject, NotificationKinds.Moved, cancellationToken);

        return new ApplicationResult.Done(application);
    }
}
