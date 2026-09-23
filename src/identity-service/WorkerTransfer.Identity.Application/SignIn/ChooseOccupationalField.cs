using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.SignIn;

/// <summary>A person names their occupational field — or takes the statement back.</summary>
/// <param name="Subject">Whose account. Always the caller's own.</param>
/// <param name="OccupationalField">What they chose, or <c>null</c> for "none".</param>
public sealed record ChooseOccupationalFieldCommand(SubjectId Subject, OccupationalField? OccupationalField)
    : ICommandMessage<bool>;

/// <inheritdoc cref="ChooseOccupationalFieldCommand" />
/// <remarks>
/// Only one's own account — which is why there is no id in the body. <c>null</c>
/// means remove, or a choice once made would be final. The occupational field
/// is never concluded from behaviour (ADR-0039).
/// </remarks>
public sealed class ChooseOccupationalFieldHandler(IUserRepository users)
    : IRequestHandler<ChooseOccupationalFieldCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        ChooseOccupationalFieldCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByIdAsync(request.Subject, cancellationToken);

        if (account is null)
        {
            return false;
        }

        account.ChooseOccupationalField(request.OccupationalField);

        // Aggregates come back detached: without this call the choice never
        // reaches the database.
        await users.SaveAsync(account, cancellationToken);

        return true;
    }
}
