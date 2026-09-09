using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Carry a sign-in forward with the refresh token presented.</summary>
public sealed record ErneuernBefehl(string Erneuerungstoken) : IBefehl<Erneuerungsergebnis>;

/// <summary>What a refresh produced.</summary>
public abstract record Erneuerungsergebnis
{
    private Erneuerungsergebnis() { }

    /// <summary>A fresh pair. The presented refresh token no longer counts.</summary>
    public sealed record Erneuert(
        string Zugriffstoken,
        string Erneuerungstoken,
        DateTimeOffset ErneuerungLaeuftAb) : Erneuerungsergebnis;

    /// <summary>
    /// The presented token entitles the caller to nothing.
    /// </summary>
    /// <remarks>
    /// Expired, already used, never issued, or belonging to an account that may
    /// no longer sign in — one answer for all of them, and the caller is told to
    /// drop the cookie it presented.
    /// </remarks>
    public sealed record Abgewiesen : Erneuerungsergebnis;
}

/// <summary>Carries a sign-in forward.</summary>
/// <remarks>
/// Rotation is the point: every refresh retires the token it was given, so a
/// stolen one works once and the second use is visible.
/// </remarks>
public sealed class ErneuernHandler(
    ISessionService sitzungen,
    IUserRepository benutzer,
    IMembershipRepository mitgliedschaften,
    ISessionCapacity handlungsform,
    IAccessTokenIssuer token,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<ErneuernBefehl, Erneuerungsergebnis>
{
    /// <inheritdoc />
    public async Task<Erneuerungsergebnis> Handle(
        ErneuernBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sitzung = await sitzungen.RenewAsync(request.Erneuerungstoken, cancellationToken);

        if (sitzung is null)
        {
            return new Erneuerungsergebnis.Abgewiesen();
        }

        var konto = await benutzer.FindByIdAsync(sitzung.Subject, cancellationToken);

        if (konto is null || !DarfNochAnmelden(konto))
        {
            // A refresh mints a fresh access token, so it is an authorisation
            // decision and not a formality. Without this an account that was
            // disabled keeps working for as long as it keeps refreshing —
            // the Python service leaves that window open.
            return new Erneuerungsergebnis.Abgewiesen();
        }

        var form = await ErneutGeprueft(sitzung, cancellationToken);

        var zugriff = await token.IssueAsync(
            konto.Id, konto.Email, form, sitzung.Session, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.TokenRefresh,
                uhr.GetUtcNow(),
                actor: konto.Id,
                tenant: (form as Capacity.ForCompany)?.Tenant,
                correlationId: korrelation.Aktuell,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "rotation"
                }),
            cancellationToken);

        return new Erneuerungsergebnis.Erneuert(
            zugriff, sitzung.RefreshToken, sitzung.ExpiresAt);
    }

    /// <summary>
    /// The capacity this sign-in still holds — asked again rather than carried
    /// forward.
    /// </summary>
    /// <remarks>
    /// Checking the membership once, when the company token is first minted,
    /// makes the check good exactly once: whoever was let in stays in for as
    /// long as they keep refreshing, long after the company removed them.
    /// <para>
    /// Only the company falls away, never the sign-in. The person is still
    /// signed in and simply acts as themselves again, which is the default
    /// state (ADR-0017) — ending the session would sign someone out of their
    /// personal account because a company relationship ended.
    /// </para>
    /// </remarks>
    private async Task<Capacity> ErneutGeprueft(
        RenewedSession sitzung,
        CancellationToken cancellationToken)
    {
        var form = await handlungsform.RecallAsync(sitzung.Session, cancellationToken);

        if (form is not Capacity.ForCompany firma)
        {
            return form;
        }

        if (await mitgliedschaften.IsMemberAsync(sitzung.Subject, firma.Tenant, cancellationToken))
        {
            return form;
        }

        await handlungsform.RememberAsync(
            sitzung.Session, Capacity.AsSelf.Instance, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.TenantSwitchDenied,
                uhr.GetUtcNow(),
                actor: sitzung.Subject,
                // The company that was dropped is the point of the record.
                tenant: firma.Tenant,
                correlationId: korrelation.Aktuell,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "membership_gone_on_refresh"
                }),
            cancellationToken);

        return Capacity.AsSelf.Instance;
    }

    private static bool DarfNochAnmelden(User konto)
    {
        try
        {
            konto.AssertCanSignIn();
            return true;
        }
        catch (EmailNotConfirmedException)
        {
            return false;
        }
        catch (AccountDisabledException)
        {
            return false;
        }
    }
}
