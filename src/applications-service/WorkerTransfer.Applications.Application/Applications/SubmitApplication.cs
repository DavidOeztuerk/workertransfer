using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Messages;
using WorkerTransfer.Outbox;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Applying — and with it releasing the data to this one company.</summary>
public sealed record SubmitApplicationCommand(
    Guid Job,
    SubjectId Subject,
    string Message,
    bool SharesResume,
    bool SharesPortfolio) : ICommandMessage<ApplicationResult>;

/// <summary>Creates the case and grants the release for it.</summary>
/// <remarks>
/// The order is the actual decision: first the ledger, then the case, then the
/// commit. If the ledger fails, <see cref="ConsentGateSilent"/> flies through
/// the transaction and nothing is committed — otherwise there would be an
/// application the company may not read. If the ledger succeeds and the commit
/// fails, the company would have access without a visible application; that is
/// why the withdrawal revokes unconditionally, and every outcome leads into a
/// clean state.
/// <para>
/// The release comes about in the ledger, not in this database, and it names
/// the recipient. It is the same mechanism as for the résumé, only triggered by
/// a different act.
/// </para>
/// </remarks>
public sealed class SubmitApplicationHandler(
    IApplicationStore store,
    IJobLookup jobs,
    IConsentWriter ledger,
    IOutbox outbox,
    ICompanyMemberLookup members,
    IContactLookup contact,
    TimeProvider clock) : IRequestHandler<SubmitApplicationCommand, ApplicationResult>
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

        // Members FIRST: if identity fails, the ledger must grant nothing. The
        // lookup is not the mail (ADR-0025); a release without a case would be
        // the gap the withdrawal would otherwise have to clear.
        var companyMembers = await members.GetAsync(job.Company, cancellationToken);

        var now = clock.GetUtcNow();
        var enclosures = new Enclosures(request.SharesResume, request.SharesPortfolio);
        var letterhead = await contact.GetContactAsync(cancellationToken);
        var existing = await store.GetAsync(job.Id, request.Subject, cancellationToken);

        JobApplication application;

        try
        {
            if (existing is null)
            {
                application = JobApplication.Submit(
                    job.Id,
                    // Taken from the job once and never touched again — a job
                    // does not change its company.
                    job.Company,
                    request.Subject,
                    request.Message,
                    enclosures,
                    now,
                    letterhead);
            }
            else
            {
                existing.Resubmit(request.Message, enclosures, now, letterhead);
                application = existing;
            }
        }
        catch (TransitionNotAllowed error)
        {
            return new ApplicationResult.StateConflict(error.Message);
        }
        catch (InputError error)
        {
            return new ApplicationResult.InvalidInput(error.Message);
        }

        await ledger.GrantAsync(
            request.Subject,
            ConsentKey.For(
                application.Company, enclosures.CurriculumVitae, enclosures.Portfolio),
            cancellationToken);

        await store.SaveAsync(application, cancellationToken);

        // Notify the company: one outbox row per member. The outbox stays free
        // of content (ADR-0025): id and kind, never a name, never a job, never
        // an email address.
        foreach (var member in companyMembers)
        {
            await outbox.NoteAsync(member, NotificationKinds.Received, cancellationToken);
        }

        return new ApplicationResult.Done(application);
    }
}
