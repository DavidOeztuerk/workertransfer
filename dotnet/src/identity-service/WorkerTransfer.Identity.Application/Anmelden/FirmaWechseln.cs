using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Act for a company from now on.</summary>
/// <remarks>
/// The caller may name any company; membership decides. That is what keeps the
/// tenant out of client control even though the client names it — the tenant
/// that reaches a token was never taken from the request, it was derived from a
/// checked membership.
/// </remarks>
/// <param name="Wer">Who is asking — from the token, never from the body.</param>
/// <param name="Firma">The company they name.</param>
public sealed record FirmaWechselnBefehl(SubjectId Wer, TenantId Firma)
    : IBefehl<Firmenwechselergebnis>;

/// <summary>What the attempt to act for a company produced.</summary>
public abstract record Firmenwechselergebnis
{
    private Firmenwechselergebnis() { }

    /// <summary>A fresh pair, acting for the company.</summary>
    public sealed record Gewechselt(
        string Zugriffstoken,
        string Erneuerungstoken,
        DateTimeOffset ErneuerungLaeuftAb) : Firmenwechselergebnis;

    /// <summary>
    /// The caller is not a member of this company.
    /// </summary>
    /// <remarks>
    /// Answered the same whether or not the company exists. A distinct answer
    /// for "no such company" would let anyone probe which companies are here.
    /// </remarks>
    public sealed record KeinMitglied : Firmenwechselergebnis;

    /// <summary>The account may not act at all.</summary>
    public sealed record Abgelehnt : Firmenwechselergebnis;
}

/// <summary>Mints a company-bound pair after verifying membership.</summary>
public sealed class FirmaWechselnHandler(
    IMembershipRepository mitgliedschaften,
    IUserRepository benutzer,
    ISessionService sitzungen,
    ISessionCapacity handlungsform,
    IAccessTokenIssuer token,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<FirmaWechselnBefehl, Firmenwechselergebnis>
{
    /// <inheritdoc />
    public async Task<Firmenwechselergebnis> Handle(
        FirmaWechselnBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await mitgliedschaften.IsMemberAsync(request.Wer, request.Firma, cancellationToken))
        {
            await protokoll.AppendAsync(
                new AuditEvent(
                    AuditAction.TenantSwitchDenied,
                    uhr.GetUtcNow(),
                    actor: request.Wer,
                    // The company the caller asked for is the point of the
                    // record — especially because it was refused.
                    tenant: request.Firma,
                    correlationId: korrelation.Aktuell),
                cancellationToken);

            return new Firmenwechselergebnis.KeinMitglied();
        }

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken);

        if (konto is null)
        {
            return new Firmenwechselergebnis.Abgelehnt();
        }

        try
        {
            konto.AssertCanSignIn();
        }
        catch (Exception fehler) when (fehler is EmailNotConfirmedException
                                              or AccountDisabledException)
        {
            return new Firmenwechselergebnis.Abgelehnt();
        }

        // A fresh sign-in rather than an edit of the old one: the personal
        // refresh token keeps working, and the company-bound one can be ended
        // on its own.
        var sitzung = await sitzungen.StartAsync(konto.Id, cancellationToken);
        var form = new Capacity.ForCompany(request.Firma);

        await handlungsform.RememberAsync(sitzung.Session, form, cancellationToken);

        var zugriff = await token.IssueAsync(
            konto.Id, konto.Email, form, sitzung.Session, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.TenantSwitch,
                uhr.GetUtcNow(),
                actor: konto.Id,
                tenant: request.Firma,
                correlationId: korrelation.Aktuell),
            cancellationToken);

        return new Firmenwechselergebnis.Gewechselt(
            zugriff, sitzung.RefreshToken, sitzung.ExpiresAt);
    }
}
