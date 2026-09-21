using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>A person picks the language they are written to in.</summary>
/// <param name="Subject">Whose account. Always the caller's own.</param>
/// <param name="Language">What they picked.</param>
public sealed record ChooseLanguageCommand(SubjectId Subject, AccountLanguage Language)
    : ICommand<bool>;

/// <inheritdoc cref="ChooseLanguageCommand" />
/// <remarks>
/// <strong>Self only, and there is no id in the request.</strong> Writing to
/// somebody else's account is not a feature this needs, and an id would be the
/// only thing standing between a caller and that.
/// <para>
/// A command and not a setting on the request: the browser's header is a guess,
/// and a guess must never quietly overwrite a decision. Somebody who reads
/// German while travelling in France should not find their mails in French
/// afterwards.
/// </para>
/// </remarks>
public sealed class ChooseLanguageHandler(IUserRepository users)
    : IRequestHandler<ChooseLanguageCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        ChooseLanguageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByIdAsync(request.Subject, cancellationToken);

        if (account is null)
        {
            return false;
        }

        account.ChooseLanguage(request.Language);

        // Aggregates come back detached from the repository, so the write
        // reaches the database only through this call. Forgetting it costs
        // nothing at test time and silently loses the choice in production.
        await users.SaveAsync(account, cancellationToken);

        return true;
    }
}
