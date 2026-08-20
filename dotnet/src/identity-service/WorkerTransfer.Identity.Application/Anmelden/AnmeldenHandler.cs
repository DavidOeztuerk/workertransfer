using Girder.Abstractions.Security.Passwords;
using Girder.Core.Identity;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>What a sign-in attempt produced.</summary>
public abstract record Anmeldeergebnis
{
    private Anmeldeergebnis() { }

    /// <summary>Signed in. Both tokens are here and nowhere else.</summary>
    public sealed record Angemeldet(
        string Zugriffstoken,
        string Erneuerungstoken,
        DateTimeOffset ErneuerungLaeuftAb) : Anmeldeergebnis;

    /// <summary>
    /// Wrong address, wrong password, or an account that may not sign in.
    /// </summary>
    /// <remarks>
    /// One answer for all of them. A separate one per case answers "is this
    /// person here?" to whoever asks, and on a transfer market that is the
    /// question that costs someone their job.
    /// </remarks>
    public sealed record Abgelehnt : Anmeldeergebnis;

    /// <summary>The password was right and the address is not confirmed yet.</summary>
    /// <remarks>
    /// Its own answer, so the interface can offer to send the mail again.
    /// Reached only after the password verified, so it states nothing the
    /// password did not already prove.
    /// </remarks>
    public sealed record NichtBestaetigt : Anmeldeergebnis;
}

/// <summary>Signs a person in.</summary>
public sealed class AnmeldenHandler(
    IUserRepository benutzer,
    IPasswordHasher passwoerter,
    ISessionService sitzungen,
    IAccessTokenIssuer token)
{
    /// <param name="email">The address presented.</param>
    /// <param name="passwort">The password presented.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    public async Task<Anmeldeergebnis> HandleAsync(
        string email,
        string passwort,
        CancellationToken cancellationToken = default)
    {
        var konto = await benutzer.FindByEmailAsync(email, cancellationToken);

        // Null is a supported input: the implementation spends the same work
        // before answering Failed, so how long an answer takes says nothing
        // about whether the address is known.
        if (passwoerter.Verify(passwort, konto?.PasswordHash) == PasswordVerification.Failed
            || konto is null)
        {
            return new Anmeldeergebnis.Abgelehnt();
        }

        try
        {
            konto.AssertCanSignIn();
        }
        catch (EmailNotConfirmedException)
        {
            return new Anmeldeergebnis.NichtBestaetigt();
        }
        catch (AccountDisabledException)
        {
            return new Anmeldeergebnis.Abgelehnt();
        }

        var sitzung = await sitzungen.StartAsync(konto.Id, cancellationToken);

        // Signing in makes you yourself, never a company. Acting for one is a
        // second step that verifies membership before a tenant reaches a token.
        var zugriff = await token.IssueAsync(
            konto.Id,
            konto.Email,
            Capacity.AsSelf.Instance,
            sitzung.Session,
            cancellationToken);

        return new Anmeldeergebnis.Angemeldet(
            zugriff, sitzung.RefreshToken, sitzung.ExpiresAt);
    }
}
