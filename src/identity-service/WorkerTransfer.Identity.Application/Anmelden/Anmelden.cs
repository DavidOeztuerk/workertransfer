using Girder.Abstractions.Security.Passwords;
using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Sign in with an address and a password.</summary>
public sealed record AnmeldenBefehl(string Email, string Passwort) : IBefehl<Anmeldeergebnis>;

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
    /// question that costs someone their job. Which case it was is in the audit
    /// trail, where only an operator sees it.
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
    IAccessTokenIssuer token,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<AnmeldenBefehl, Anmeldeergebnis>
{
    /// <inheritdoc />
    public async Task<Anmeldeergebnis> Handle(
        AnmeldenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await benutzer.FindByEmailAsync(request.Email, cancellationToken);

        // Null is a supported input: the implementation spends the same work
        // before answering Failed, so how long an answer takes says nothing
        // about whether the address is known. Reordering these two operands
        // brings the timing channel back.
        if (passwoerter.Verify(request.Passwort, konto?.PasswordHash) == PasswordVerification.Failed
            || konto is null)
        {
            await NotiereAbweisung(
                konto is null ? "unknown_user" : "bad_password", konto?.Id, cancellationToken);
            return new Anmeldeergebnis.Abgelehnt();
        }

        try
        {
            konto.AssertCanSignIn();
        }
        catch (EmailNotConfirmedException)
        {
            await NotiereAbweisung("email_not_confirmed", konto.Id, cancellationToken);
            return new Anmeldeergebnis.NichtBestaetigt();
        }
        catch (AccountDisabledException)
        {
            await NotiereAbweisung("disabled", konto.Id, cancellationToken);
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

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.LoginSuccess,
                uhr.GetUtcNow(),
                actor: konto.Id,
                correlationId: korrelation.Aktuell),
            cancellationToken);

        return new Anmeldeergebnis.Angemeldet(
            zugriff, sitzung.RefreshToken, sitzung.ExpiresAt);
    }

    /// <summary>
    /// Records why a sign-in was refused — the one place the four cases are
    /// told apart, and it is not the answer.
    /// </summary>
    private Task NotiereAbweisung(
        string grund,
        SubjectId? wer,
        CancellationToken cancellationToken) =>
        protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.LoginFailure,
                uhr.GetUtcNow(),
                actor: wer,
                correlationId: korrelation.Aktuell,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = grund
                }),
            cancellationToken);
}
