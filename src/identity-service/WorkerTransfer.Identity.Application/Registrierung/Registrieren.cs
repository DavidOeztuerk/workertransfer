using Girder.Abstractions.Security.Passwords;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Register an account.</summary>
/// <param name="Firmenname">
/// Set means "a company is registering here". It becomes a company only when
/// the address is confirmed — until then it is an intention (E2.6).
/// </param>
public sealed record RegistrierenBefehl(
    string Email,
    string Passwort,
    string Anzeigename,
    string? Firmenname = null) : IBefehl<Registrierergebnis>;

/// <summary>What registering produced.</summary>
public abstract record Registrierergebnis
{
    private Registrierergebnis() { }

    /// <summary>
    /// Taken. Answered whether or not the address was already known.
    /// </summary>
    /// <remarks>
    /// One case for both, deliberately, and it is a single type so no endpoint
    /// can accidentally tell them apart. A 409 would answer "is this person
    /// here?" without asking the consent ledger — on a transfer market, the
    /// question that costs somebody their job. The real owner is told instead,
    /// by mail.
    /// </remarks>
    public sealed record Angenommen : Registrierergebnis;

    /// <summary>The password does not clear the floor.</summary>
    public sealed record SchwachesPasswort(string Grund) : Registrierergebnis;

    /// <summary>A mass provider cannot be claimed as a company.</summary>
    public sealed record OeffentlicheDomain(string Domain) : Registrierergebnis;
}

/// <summary>Takes a registration.</summary>
public sealed class RegistrierenHandler(
    IUserRepository benutzer,
    IPasswordHasher passwoerter,
    IVerificationTokenRepository tokens,
    IEinmaltoken einmaltoken,
    IPostkorb postkorb,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<RegistrierenBefehl, Registrierergebnis>
{
    /// <summary>How long a confirmation link works.</summary>
    private static readonly TimeSpan Gueltigkeit = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<Registrierergebnis> Handle(
        RegistrierenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            PasswordPolicy.Validate(request.Passwort);
        }
        catch (WeakPasswordException schwach)
        {
            return new Registrierergebnis.SchwachesPasswort(schwach.Reason);
        }

        // Freemail BEFORE the existence check, and that is not a matter of
        // style: checked afterwards, the endpoint would answer differently for
        // a known address than for an unknown one — and the promise of "the
        // same answer" would be gone.
        //
        // Nothing is revealed by it: whether an address sits with a mass
        // provider is in the address the person just typed themselves.
        //
        // The domain ITSELF is not checked here. "firma.de is already claimed"
        // would be an enumeration channel over companies, answerable by anyone
        // who guesses a domain. That waits for the confirmation, when the
        // address is proven.
        if (request.Firmenname is not null)
        {
            var domain = EmailDomain.FromEmail(request.Email);

            if (domain.IsPublic)
            {
                return new Registrierergebnis.OeffentlicheDomain(domain.Value);
            }
        }

        // Always hash, even when the address has long been taken. bcrypt with
        // twelve rounds costs a few hundred milliseconds; an early exit would
        // be back in ten and would give away over the clock what the identical
        // status code is hiding. Otherwise the enumeration channel just moves
        // into the wristwatch.
        var eintrag = passwoerter.Hash(request.Passwort);

        var vorhanden = await benutzer.FindByEmailAsync(request.Email, cancellationToken);

        if (vorhanden is not null)
        {
            postkorb.Doppelanmeldung(vorhanden.Email, vorhanden.Id);
            return new Registrierergebnis.Angenommen();
        }

        var konto = User.Register(
            request.Email, eintrag, request.Anzeigename, request.Firmenname);

        await benutzer.AddAsync(konto, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.Register,
                uhr.GetUtcNow(),
                // Registering is an act of a person, not of a company (ADR-0017).
                actor: konto.Id,
                correlationId: korrelation.Aktuell),
            cancellationToken);

        var (klartext, hash) = einmaltoken.Erzeuge();

        await tokens.AddAsync(
            new VerificationToken(
                Guid.CreateVersion7(),
                konto.Id,
                hash,
                TokenPurpose.EmailVerify,
                uhr.GetUtcNow() + Gueltigkeit,
                ConsumedAt: null),
            cancellationToken);

        postkorb.Bestaetigungslink(konto.Email, konto.Id, klartext);

        return new Registrierergebnis.Angenommen();
    }
}
