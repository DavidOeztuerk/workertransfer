using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Who am I?</summary>
/// <remarks>
/// A protected resource, so an anonymous caller gets 401 — the opposite of
/// <see cref="SessionStateQuery"/>, which asks a public question.
/// </remarks>
public sealed record MyAccountQuery : IQuery<AccountView?>;

/// <summary>Answers with the caller's own account.</summary>
public sealed class MyAccountHandler(ICurrentPrincipal principal, IUserRepository users)
    : IRequestHandler<MyAccountQuery, AccountView?>
{
    /// <inheritdoc />
    public async Task<AccountView?> Handle(
        MyAccountQuery request,
        CancellationToken cancellationToken)
    {
        if (principal.Current is not { } actor)
        {
            return null;
        }

        var account = await users.FindByIdAsync(actor.Subject, cancellationToken);

        return new AccountView(
            actor.Subject,
            account?.Email,
            actor.Acting is Capacity.ForCompany company ? company.Tenant : null,
            account?.AccountLanguage ?? LanguageChoice.Default,
            account?.DisplayName ?? string.Empty,
            account?.GivenName,
            account?.FamilyName,
            account?.OccupationalField);
    }
}
