using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Is anyone signed in right now?</summary>
/// <param name="RefreshPresent">
/// Whether the caller presented a refresh token. Not whether it still carries —
/// that is what refreshing itself decides, and a second place that judges
/// tokens is a second place that can judge them wrong.
/// </param>
public sealed record SessionStateQuery(bool RefreshPresent) : IQuery<SessionState>;

/// <summary>Where a visitor stands.</summary>
/// <param name="Account">The account, when one is signed in.</param>
/// <param name="State">
/// <c>active</c>, <c>renewable</c> or <c>anonymous</c>.
/// </param>
public sealed record SessionState(AccountView? Account, string State)
{
    /// <summary>Signed in; the account is here.</summary>
    public static SessionState Active(AccountView account) => new(account, "active");

    /// <summary>
    /// The access token no longer carries, but a refresh token was presented.
    /// </summary>
    /// <remarks>
    /// The reason this query exists at all. Only with this state can the
    /// interface call the refresh endpoint deliberately instead of on the
    /// off-chance.
    /// </remarks>
    public static SessionState Renewable { get; } = new(null, "renewable");

    /// <summary>Nobody is signed in, and nothing suggests anyone could be.</summary>
    public static SessionState Anonymous { get; } = new(null, "anonymous");
}

/// <summary>Answers the state of the current request.</summary>
/// <remarks>
/// Public, and always answers 200 — including for anonymous visitors. Asking
/// this through <c>/me</c> would produce a 401 for every signed-out visitor,
/// which is a failure entry about a completely normal state.
/// </remarks>
public sealed class SessionStateHandler(ICurrentPrincipal principal, IUserRepository users)
    : IRequestHandler<SessionStateQuery, SessionState>
{
    /// <inheritdoc />
    public async Task<SessionState> Handle(
        SessionStateQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (principal.Current is { } actor)
        {
            var account = await users.FindByIdAsync(actor.Subject, cancellationToken);

            return SessionState.Active(new AccountView(
                actor.Subject,
                account?.Email,
                actor.Acting is Capacity.ForCompany company ? company.Tenant : null,
                account?.AccountLanguage ?? LanguageChoice.Default,
                account?.DisplayName ?? string.Empty,
                account?.GivenName,
                account?.FamilyName,
                account?.OccupationalField));
        }

        // Without a sign-in the answer is the same whatever cookies came with
        // it: this endpoint is public and must reveal nothing anyone could
        // conclude the existence of an account from.
        return request.RefreshPresent ? SessionState.Renewable : SessionState.Anonymous;
    }
}
