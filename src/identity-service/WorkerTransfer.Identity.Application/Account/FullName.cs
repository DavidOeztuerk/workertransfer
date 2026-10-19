using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Account;

/// <summary>Set the civil given and family name. Always only one's own.</summary>
public sealed record SetFullNameCommand(SubjectId Subject, string? GivenName, string? FamilyName)
    : ICommandMessage<bool>;

/// <inheritdoc cref="SetFullNameCommand" />
public sealed class SetFullNameHandler(IUserRepository users)
    : IRequestHandler<SetFullNameCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        SetFullNameCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByIdAsync(request.Subject, cancellationToken);
        if (account is null)
        {
            return false;
        }

        account.SetFullName(request.GivenName, request.FamilyName);
        await users.SaveAsync(account, cancellationToken);
        return true;
    }
}
