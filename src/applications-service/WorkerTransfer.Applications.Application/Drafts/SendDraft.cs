using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Messages;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Application.Drafts;

/// <summary>Send the approved draft as an application.</summary>
/// <remarks>
/// <strong>Approving and sending are two acts.</strong> A button "approve and
/// send" saves a click and takes the approval's meaning away: it is the moment
/// in which somebody says "like this, and not otherwise", and that moment needs
/// its own (ADR-0034).
/// </remarks>
public sealed record SendDraftCommand(SubjectId Subject, Guid Id) : ICommandMessage<DraftResult>;

/// <summary>Turns a draft into an application.</summary>
public sealed class SendDraftHandler(
    IDraftStore drafts,
    IApplicationStore applications,
    IJobLookup jobs,
    IConsentWriter ledger,
    IOutbox outbox,
    ICompanyMemberLookup members,
    IContactLookup contact,
    TimeProvider clock) : IRequestHandler<SendDraftCommand, DraftResult>
{
    /// <inheritdoc />
    public async Task<DraftResult> Handle(
        SendDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await drafts.GetAsync(request.Subject, request.Id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        if (draft.State != DraftState.Approved)
        {
            return new DraftResult.StateConflict("Only an approved draft goes out.");
        }

        // Ask for the job once more: days can lie between the draft and the
        // sending, and a closed advert takes no application any more.
        var job = await jobs.GetAsync(draft.Job, cancellationToken);

        if (job is null)
        {
            return new DraftResult.StateConflict("This job is no longer advertised.");
        }

        var companyMembers = await members.GetAsync(job.Company, cancellationToken);

        var now = clock.GetUtcNow();
        var enclosures = new Enclosures(
            draft.SharesResume, Portfolio: false, draft.Documents);
        var letterhead = await contact.GetContactAsync(cancellationToken);

        var existing = await applications.GetAsync(job.Id, request.Subject, cancellationToken);
        JobApplication application;

        try
        {
            if (existing is null)
            {
                application = JobApplication.Submit(
                    job.Id, job.Company, request.Subject, draft.Text, enclosures, now,
                    letterhead);
            }
            else
            {
                existing.Resubmit(draft.Text, enclosures, now, letterhead);
                application = existing;
            }
        }
        catch (TransitionNotAllowed error)
        {
            return new DraftResult.StateConflict(error.Message);
        }
        catch (InputError error)
        {
            return new DraftResult.InvalidInput(error.Message);
        }

        // FIRST THE LEDGER, THEN THE CASE, THEN THE COMMIT — the same order as
        // for ordinary sending, and for the same reason: there must be no
        // application the company may not read.
        await ledger.GrantAsync(
            request.Subject,
            ConsentKey.For(
                application.Company,
                enclosures.CurriculumVitae,
                enclosures.Portfolio,
                enclosures.Documents.Count > 0),
            cancellationToken);

        await applications.SaveAsync(application, cancellationToken);

        draft.MarkSent(now);
        await drafts.SaveAsync(draft, cancellationToken);

        // The person learns that her application is out.
        await outbox.NoteAsync(
            request.Subject, NotificationKinds.Moved, cancellationToken);

        foreach (var member in companyMembers)
        {
            await outbox.NoteAsync(member, NotificationKinds.Received, cancellationToken);
        }

        return new DraftResult.Done(draft);
    }
}
