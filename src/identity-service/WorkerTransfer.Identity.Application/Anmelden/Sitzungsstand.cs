using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Is anyone signed in right now?</summary>
/// <param name="ErneuerungLiegtVor">
/// Whether the caller presented a refresh token. Not whether it still carries —
/// that is what refreshing itself decides, and a second place that judges
/// tokens is a second place that can judge them wrong.
/// </param>
public sealed record SitzungsstandAbfrage(bool ErneuerungLiegtVor) : IAbfrage<Sitzungsstand>;

/// <summary>Where a visitor stands.</summary>
/// <param name="Benutzer">The account, when one is signed in.</param>
/// <param name="Zustand">
/// <c>active</c>, <c>renewable</c> or <c>anonymous</c>.
/// </param>
public sealed record Sitzungsstand(Kontoansicht? Benutzer, string Zustand)
{
    /// <summary>Signed in; the account is here.</summary>
    public static Sitzungsstand Aktiv(Kontoansicht konto) => new(konto, "active");

    /// <summary>
    /// The access token no longer carries, but a refresh token was presented.
    /// </summary>
    /// <remarks>
    /// The reason this query exists at all. Only with this state can the
    /// interface call the refresh endpoint deliberately instead of on the
    /// off-chance.
    /// </remarks>
    public static Sitzungsstand Erneuerbar { get; } = new(null, "renewable");

    /// <summary>Nobody is signed in, and nothing suggests anyone could be.</summary>
    public static Sitzungsstand Anonym { get; } = new(null, "anonymous");
}

/// <summary>Answers the state of the current request.</summary>
/// <remarks>
/// Public, and always answers 200 — including for anonymous visitors. Asking
/// this through <c>/me</c> would produce a 401 for every signed-out visitor,
/// which is a failure entry about a completely normal state.
/// </remarks>
public sealed class SitzungsstandHandler(ICurrentPrincipal akteur, IUserRepository benutzer)
    : IRequestHandler<SitzungsstandAbfrage, Sitzungsstand>
{
    /// <inheritdoc />
    public async Task<Sitzungsstand> Handle(
        SitzungsstandAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (akteur.Current is { } handelnder)
        {
            var konto = await benutzer.FindByIdAsync(handelnder.Subject, cancellationToken);

            return Sitzungsstand.Aktiv(new Kontoansicht(
                handelnder.Subject,
                konto?.Email,
                handelnder.Acting is Capacity.ForCompany firma ? firma.Tenant : null,
                konto?.Kontosprache ?? Sprachwahl.Vorgabe));
        }

        // Without a sign-in the answer is the same whatever cookies came with
        // it: this endpoint is public and must reveal nothing anyone could
        // conclude the existence of an account from.
        return request.ErneuerungLiegtVor ? Sitzungsstand.Erneuerbar : Sitzungsstand.Anonym;
    }
}
