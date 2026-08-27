using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Confirm an address with the token from the mail.</summary>
public sealed record AdresseBestaetigenBefehl(string Token) : IBefehl<Bestaetigungsergebnis>;

/// <summary>What confirming produced.</summary>
public abstract record Bestaetigungsergebnis
{
    private Bestaetigungsergebnis() { }

    /// <summary>
    /// The account is active.
    /// </summary>
    /// <remarks>
    /// Three exits, not two: confirmed · confirmed <em>with</em> a company ·
    /// confirmed <em>without</em> one and why. The third is the one an
    /// interface otherwise shows as "all good" while half the intention has
    /// evaporated.
    /// </remarks>
    /// <param name="Firmenname">The company that was created, if one was.</param>
    /// <param name="Firmenfehler">Why none was, if the intention was refused.</param>
    public sealed record Bestaetigt(string? Firmenname = null, string? Firmenfehler = null)
        : Bestaetigungsergebnis;

    /// <summary>The link means nothing.</summary>
    public sealed record Ungueltig : Bestaetigungsergebnis;

    /// <summary>The link is past its time.</summary>
    public sealed record Abgelaufen : Bestaetigungsergebnis;
}

/// <summary>Confirms an address, and redeems the company intention with it.</summary>
public sealed class AdresseBestaetigenHandler(
    IUserRepository benutzer,
    IVerificationTokenRepository tokens,
    IEinmaltoken einmaltoken,
    UnternehmenAnlegen unternehmen,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<AdresseBestaetigenBefehl, Bestaetigungsergebnis>
{
    /// <inheritdoc />
    public async Task<Bestaetigungsergebnis> Handle(
        AdresseBestaetigenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jetzt = uhr.GetUtcNow();
        var zeile = await tokens.FindByHashAsync(
            einmaltoken.Hashe(request.Token), cancellationToken);

        if (zeile is not { Purpose: TokenPurpose.EmailVerify })
        {
            // An unknown token says nothing about why it is unknown, or the
            // endpoint becomes an oracle.
            return new Bestaetigungsergebnis.Ungueltig();
        }

        var konto = await benutzer.FindByIdAsync(zeile.Subject, cancellationToken);

        if (konto is null)
        {
            return new Bestaetigungsergebnis.Ungueltig();
        }

        if (zeile.IsConsumed)
        {
            // Clicking the same link twice is not an error: the account is
            // exactly as unlocked as the click wanted it. Whoever holds the
            // token had the mail, so nothing is revealed here.
            //
            // Still pending means the token was voided by a resend, and the old
            // link must not unlock anything any more.
            return konto.Status == AccountStatus.Active
                ? new Bestaetigungsergebnis.Bestaetigt()
                : new Bestaetigungsergebnis.Ungueltig();
        }

        if (zeile.IsExpired(jetzt))
        {
            return new Bestaetigungsergebnis.Abgelaufen();
        }

        konto.Activate();

        // Without this the unlock stays in memory: the aggregate comes back
        // detached from the repository.
        await benutzer.SaveAsync(konto, cancellationToken);
        await tokens.ConsumeAsync(zeile.Id, jetzt, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.EmailVerified,
                jetzt,
                actor: konto.Id,
                correlationId: korrelation.Aktuell),
            cancellationToken);

        return konto.PendingCompanyName is { } gewollt
            ? await LoeseAbsichtEin(konto, gewollt, cancellationToken)
            : new Bestaetigungsergebnis.Bestaetigt();
    }

    /// <summary>
    /// Now — and only now — the intention from the registration can be
    /// redeemed.
    /// </summary>
    /// <remarks>
    /// The account is active, so the company creation accepts it, and the
    /// domain comes from a <em>proven</em> address (ADR-0019). Same
    /// transaction, same repositories, no service-to-service call: companies
    /// live in this service.
    /// </remarks>
    private async Task<Bestaetigungsergebnis> LoeseAbsichtEin(
        User konto,
        string gewollt,
        CancellationToken cancellationToken)
    {
        var ergebnis = await unternehmen.AusfuehrenAsync(konto, gewollt, cancellationToken);

        // Spent in EVERY case, including a refusal — see User.CompanyIntentSpent.
        konto.CompanyIntentSpent();
        await benutzer.SaveAsync(konto, cancellationToken);

        return ergebnis switch
        {
            Firmenergebnis.Angelegt angelegt =>
                new Bestaetigungsergebnis.Bestaetigt(Firmenname: angelegt.Firma.Name),

            // The CONFIRMATION does not fail over this. The account is
            // unlocked, and that is right and irreversible: locking somebody
            // out because a company name was taken would be the wrong answer to
            // the wrong question.
            Firmenergebnis.Abgelehnt abgelehnt =>
                new Bestaetigungsergebnis.Bestaetigt(Firmenfehler: abgelehnt.Code),

            _ => new Bestaetigungsergebnis.Bestaetigt()
        };
    }
}
