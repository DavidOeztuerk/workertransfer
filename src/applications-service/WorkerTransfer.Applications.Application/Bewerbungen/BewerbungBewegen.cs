using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Das Unternehmen bewegt die JobApplication durch das Verfahren.</summary>
public sealed record MoveApplicationCommand(Guid Id, TenantId Firma, string State)
    : IBefehl<ApplicationResult>;

/// <summary>Setzt den State und vermerkt die Benachrichtigung.</summary>
public sealed class MoveApplicationHandler(
    IApplicationStore store,
    IOutbox outbox,
    TimeProvider uhr) : IRequestHandler<MoveApplicationCommand, ApplicationResult>
{
    /// <inheritdoc />
    public async Task<ApplicationResult> Handle(
        MoveApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await store.GetAsync(request.Id, cancellationToken);

        // Eine fremde JobApplication ist von außen wie keine.
        if (application is null || application.Firma != request.Firma)
        {
            return new ApplicationResult.Unbekannt();
        }

        // Ein Standwort, das es nicht gibt, lässt der Vertrag nicht durch —
        // aber ein Handler soll auch ohne ihn nicht abstürzen.
        if (!Enum.TryParse<ApplicationState>(request.State, ignoreCase: true, out var gewollt)
            || !Enum.IsDefined(gewollt))
        {
            return new ApplicationResult.Eingabe(
                "That is not a status an application can have");
        }

        try
        {
            application.Bewege(gewollt, uhr.GetUtcNow());
        }
        catch (UebergangNichtErlaubt error)
        {
            return new ApplicationResult.StateConflict(error.Message);
        }

        await store.SaveAsync(application, cancellationToken);

        // In DERSELBEN Transaktion wie der Zug (ADR-0025). Vorher stand hier
        // ein HTTP-Aufruf nach dem Commit, dessen Fehler geschluckt wurde — die
        // Zusage „eine misslungene Mail darf den Case nicht kippen" galt,
        // aber der Preis war, dass die Nachricht dann für immer weg war.
        await outbox.NoteAsync(application.Wer, Benachrichtigungsarten.Bewegt, cancellationToken);

        return new ApplicationResult.Erledigt(application);
    }
}
