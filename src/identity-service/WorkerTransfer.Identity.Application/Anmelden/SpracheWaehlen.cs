using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>A person picks the language they are written to in.</summary>
/// <param name="Wer">Whose account. Always the caller's own.</param>
/// <param name="Sprache">What they picked.</param>
public sealed record SpracheWaehlenBefehl(SubjectId Wer, Kontosprache Sprache)
    : IBefehl<bool>;

/// <inheritdoc cref="SpracheWaehlenBefehl" />
/// <remarks>
/// <strong>Self only, and there is no id in the request.</strong> Writing to
/// somebody else's account is not a feature this needs, and an id would be the
/// only thing standing between a caller and that.
/// <para>
/// A command and not a setting on the request: the browser's header is a guess,
/// and a guess must never quietly overwrite a decision. Somebody who reads
/// German while travelling in France should not find their mails in French
/// afterwards.
/// </para>
/// </remarks>
public sealed class SpracheWaehlenHandler(IUserRepository benutzer)
    : IRequestHandler<SpracheWaehlenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        SpracheWaehlenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken);

        if (konto is null)
        {
            return false;
        }

        konto.SpracheWaehlen(request.Sprache);

        // Aggregates come back detached from the repository, so the write
        // reaches the database only through this call. Forgetting it costs
        // nothing at test time and silently loses the choice in production.
        await benutzer.SaveAsync(konto, cancellationToken);

        return true;
    }
}
