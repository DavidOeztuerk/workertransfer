using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Konto;

/// <summary>Bürgerlichen Vor- und Nachnamen setzen. Immer nur die eigenen.</summary>
public sealed record KlarnameSetzenBefehl(SubjectId Wer, string? Vorname, string? Nachname)
    : IBefehl<bool>;

/// <inheritdoc cref="KlarnameSetzenBefehl" />
public sealed class KlarnameSetzenHandler(IUserRepository benutzer)
    : IRequestHandler<KlarnameSetzenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        KlarnameSetzenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken);
        if (konto is null)
        {
            return false;
        }

        konto.SetzeKlarname(request.Vorname, request.Nachname);
        await benutzer.SaveAsync(konto, cancellationToken);
        return true;
    }
}
