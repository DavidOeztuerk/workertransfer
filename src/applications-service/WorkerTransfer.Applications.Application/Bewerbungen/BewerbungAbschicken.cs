using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Outbox;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Bewerben — und damit die Daten für dieses eine Unternehmen freigeben.</summary>
public sealed record SubmitApplicationCommand(
    Guid Job,
    SubjectId Wer,
    string Nachricht,
    bool TeiltLebenslauf,
    bool TeiltPortfolio) : IBefehl<ApplicationResult>;

/// <summary>Legt den Case an und erteilt die Release dazu.</summary>
/// <remarks>
/// Die Reihenfolge ist die eigentliche Entscheidung: erst der Ledger, dann der
/// Case, dann der Commit. Schlägt der Ledger fehl, fliegt
/// <see cref="EinwilligungSchweigt"/> durch die Transaktion und es wird nichts
/// committet — es gäbe sonst eine JobApplication, die das Unternehmen nicht lesen
/// darf. Gelingt der Ledger und scheitert der Commit, hätte das Unternehmen
/// Zugriff ohne sichtbare JobApplication; deshalb widerruft der Rückzug
/// bedingungslos, und jeder Ausgang führt in einen sauberen Zustand.
/// <para>
/// Die Release entsteht im Ledger, nicht in dieser Datenbank, und sie nennt
/// den Empfänger. Es ist derselbe Mechanismus wie beim CurriculumVitae, nur
/// ausgelöst durch eine andere Handlung.
/// </para>
/// </remarks>
public sealed class SubmitApplicationHandler(
    IApplicationStore store,
    IJobLookup jobs,
    IConsentWriter ledger,
    IOutbox outbox,
    ICompanyMemberLookup members,
    IKontaktauskunft kontakt,
    TimeProvider uhr) : IRequestHandler<SubmitApplicationCommand, ApplicationResult>
{
    /// <inheritdoc />
    public async Task<ApplicationResult> Handle(
        SubmitApplicationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await jobs.GetAsync(request.Job, cancellationToken);

        if (job is null)
        {
            return new ApplicationResult.NoJob();
        }

        // Mitglieder ZUERST: scheitert identity, darf der Ledger nichts
        // erteilen. Die Search ist nicht die Mail (ADR-0025); eine Release
        // ohne Case wäre die Lücke, die der Rückzug sonst räumen müsste.
        var firmenMitglieder = await members.GetAsync(job.Firma, cancellationToken);

        var now = uhr.GetUtcNow();
        var mitgeschickt = new Mitgeschicktes(request.TeiltLebenslauf, request.TeiltPortfolio);
        var briefkopf = await kontakt.GetContactAsync(cancellationToken);
        var vorhanden = await store.GetAsync(job.Id, request.Wer, cancellationToken);

        JobApplication application;

        try
        {
            if (vorhanden is null)
            {
                application = JobApplication.Schicke_ab(
                    job.Id,
                    // Einmal aus der Job übernommen und danach nicht mehr
                    // angefasst — eine Job wechselt nicht das Unternehmen.
                    job.Firma,
                    request.Wer,
                    request.Nachricht,
                    mitgeschickt,
                    now,
                    briefkopf);
            }
            else
            {
                vorhanden.Schicke_erneut(request.Nachricht, mitgeschickt, now, briefkopf);
                application = vorhanden;
            }
        }
        catch (UebergangNichtErlaubt error)
        {
            return new ApplicationResult.StateConflict(error.Message);
        }
        catch (Eingabefehler error)
        {
            return new ApplicationResult.Eingabe(error.Message);
        }

        await ledger.GrantAsync(
            request.Wer,
            ConsentKey.Fuer(
                application.Firma, mitgeschickt.CurriculumVitae, mitgeschickt.Portfolio),
            cancellationToken);

        await store.SaveAsync(application, cancellationToken);

        // Das Unternehmen benachrichtigen: je Mitglied eine Outbox-Zeile.
        // Der Postausgang bleibt inhaltsfrei (ADR-0025): Kennung und Art,
        // nie ein Name, nie eine Job, nie eine E-Mail-Adresse.
        foreach (var member in firmenMitglieder)
        {
            await outbox.NoteAsync(member, Benachrichtigungsarten.Angekommen, cancellationToken);
        }

        return new ApplicationResult.Erledigt(application);
    }
}
