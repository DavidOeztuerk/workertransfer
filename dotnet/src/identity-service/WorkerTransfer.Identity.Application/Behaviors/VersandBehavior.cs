using MediatR;
using Microsoft.Extensions.Logging;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Application.Behaviors;

/// <summary>Sends what a command queued, once its transaction has committed.</summary>
/// <remarks>
/// Registered outside <see cref="TransaktionsBehavior{TAnfrage,TAntwort}"/>, and
/// the order is the point: sending inside the transaction would put a
/// confirmation link in somebody's inbox before the row it points at exists,
/// and a failed commit would leave them with a link to nothing.
/// <para>
/// A send that fails is logged and swallowed. It must not undo what was
/// written — the account is real, and the repair is to send the mail again.
/// </para>
/// </remarks>
public sealed class VersandBehavior<TAnfrage, TAntwort>(
    IPostkorbVersand versand,
    ILogger<VersandBehavior<TAnfrage, TAntwort>> protokoll)
    : IPipelineBehavior<TAnfrage, TAntwort>
    where TAnfrage : notnull
{
    /// <inheritdoc />
    public async Task<TAntwort> Handle(
        TAnfrage request,
        RequestHandlerDelegate<TAntwort> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var antwort = await next(cancellationToken);

        try
        {
            await versand.VersendeGesammeltesAsync(cancellationToken);
        }
        catch (Exception fehler)
        {
            // Never the recipient and never the body: a failure is exactly when
            // somebody wants to see the mail, and exactly when writing it out is
            // worst.
            protokoll.LogError(
                fehler, "Der Versand nach {Anfrage} ist gescheitert", typeof(TAnfrage).Name);
        }

        return antwort;
    }
}
