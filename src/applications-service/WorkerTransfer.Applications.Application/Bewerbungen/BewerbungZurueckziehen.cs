using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Zurückziehen — der Case bleibt, die Daten sind weg.</summary>
/// <remarks>
/// Dass jemand sich beworben und zurückgezogen hat, gehört zur Geschichte des
/// Verfahrens im Unternehmen. Die Person dahinter ist danach nicht mehr
/// einsehbar.
/// </remarks>
public sealed record WithdrawApplicationCommand(Guid Id, SubjectId Wer)
    : IBefehl<ApplicationResult>;

/// <summary>Setzt den State und widerruft die Freigaben.</summary>
public sealed class WithdrawApplicationHandler(
    IApplicationStore store,
    IConsentWriter ledger,
    TimeProvider uhr) : IRequestHandler<WithdrawApplicationCommand, ApplicationResult>
{
    /// <inheritdoc />
    public async Task<ApplicationResult> Handle(
        WithdrawApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await store.GetAsync(request.Id, cancellationToken);

        // Nicht vorhanden und nicht meins sind von außen dasselbe.
        if (application is null || application.Wer != request.Wer)
        {
            return new ApplicationResult.Unbekannt();
        }

        try
        {
            application.Ziehe_zurueck(request.Wer, uhr.GetUtcNow());
        }
        catch (UebergangNichtErlaubt error)
        {
            return new ApplicationResult.StateConflict(error.Message);
        }

        // Bedingungslos widerrufen — auch, was vielleicht gar nicht erteilt
        // wurde. Der Ledger verträgt einen Revocation ohne vorherige Erteilung,
        // und die Alternative wäre, sich auf die eigene Zeile zu verlassen, um
        // zu wissen, was im Ledger steht.
        await ledger.RevokeAsync(
            request.Wer, ConsentKey.Alles(application.Firma), cancellationToken);

        await store.SaveAsync(application, cancellationToken);

        return new ApplicationResult.Erledigt(application);
    }
}
