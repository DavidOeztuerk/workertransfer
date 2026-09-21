using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Who am I?</summary>
/// <remarks>
/// A protected resource, so an anonymous caller gets 401 — the opposite of
/// <see cref="SitzungsstandAbfrage"/>, which asks a public question.
/// </remarks>
public sealed record MyAccountQuery : IAbfrage<Kontoansicht?>;

/// <summary>Answers with the caller's own account.</summary>
public sealed class MyAccountHandler(ICurrentPrincipal akteur, IUserRepository benutzer)
    : IRequestHandler<MyAccountQuery, Kontoansicht?>
{
    /// <inheritdoc />
    public async Task<Kontoansicht?> Handle(
        MyAccountQuery request,
        CancellationToken cancellationToken)
    {
        if (akteur.Current is not { } handelnder)
        {
            return null;
        }

        var konto = await benutzer.FindByIdAsync(handelnder.Subject, cancellationToken);

        return new Kontoansicht(
            handelnder.Subject,
            konto?.Email,
            handelnder.Acting is Capacity.ForCompany company ? company.Tenant : null,
            konto?.Kontosprache ?? Sprachwahl.Vorgabe,
            konto?.DisplayName ?? string.Empty,
            konto?.GivenName,
            konto?.FamilyName,
            konto?.OccupationalField);
    }
}
