using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Registrierung;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>Create a company for the signed-in person.</summary>
/// <remarks>
/// The domain is <em>not</em> in the body. It is derived from the creator's
/// confirmed address; the client names only the name. What it cannot send, it
/// cannot forge (ADR-0017/0019).
/// <para>
/// This route stays open even though registering can carry the intention. Who
/// signs up privately and founds a company later has no other way in — an
/// invitation presupposes colleagues who do not exist yet.
/// </para>
/// </remarks>
public sealed record UnternehmenGruendenBefehl(SubjectId Wer, string Name)
    : IBefehl<Firmenergebnis>;

/// <inheritdoc cref="UnternehmenGruendenBefehl" />
public sealed class UnternehmenGruendenHandler(
    IUserRepository benutzer,
    UnternehmenAnlegen unternehmen)
    : IRequestHandler<UnternehmenGruendenBefehl, Firmenergebnis>
{
    /// <inheritdoc />
    public async Task<Firmenergebnis> Handle(
        UnternehmenGruendenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken);

        return konto is null
            ? new Firmenergebnis.Abgelehnt("account_not_confirmed")
            : await unternehmen.AusfuehrenAsync(konto, request.Name, cancellationToken);
    }
}
