using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Messages;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Drafts;

/// <summary>How a step on the draft turned out.</summary>
public abstract record DraftResult
{
    private DraftResult()
    {
    }

    /// <summary>It worked.</summary>
    public sealed record Done(ApplicationDraft Draft) : DraftResult;

    /// <summary>Does not exist — or belongs to someone else. The same from outside.</summary>
    public sealed record None : DraftResult;

    /// <summary>This step is not possible from here.</summary>
    public sealed record StateConflict(string Reason) : DraftResult;

    /// <summary>Something is wrong with the input.</summary>
    public sealed record InvalidInput(string Reason) : DraftResult;

    /// <summary>No provider is set up — and the interface says so.</summary>
    public sealed record NoProvider(string Reason) : DraftResult;
}

/// <summary>Begin a draft for several jobs at once.</summary>
/// <remarks>
/// <strong>The bulk action lies here and nowhere else.</strong> It does not
/// stand at the exit: sending happens piece by piece, after one approval each
/// (ADR-0034). Here it does no harm — drafts come about that nobody has seen and
/// that reach nobody.
/// </remarks>
public sealed record CreateDraftsCommand(SubjectId Subject, IReadOnlyList<Guid> Jobs)
    : ICommandMessage<IReadOnlyList<ApplicationDraft>>;

/// <summary>The model is to write now.</summary>
/// <remarks>
/// A step of its own and not an appendix of creating: writing takes seconds per
/// piece, and twenty of them in one request would be a request nobody can wait
/// for. The interface calls it per draft and shows how far along it is.
/// </remarks>
public sealed record WriteDraftCommand(SubjectId Subject, Guid Id) : ICommandMessage<DraftResult>;

/// <summary>One's own drafts.</summary>
public sealed record MyDraftsQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<ApplicationDraft>>;

/// <summary>One draft.</summary>
public sealed record DraftQuery(SubjectId Subject, Guid Id) : IQueryMessage<ApplicationDraft?>;

/// <summary>Change the text oneself.</summary>
public sealed record ChangeDraftCommand(
    SubjectId Subject, Guid Id, string? SubjectLine, string? Text)
    : ICommandMessage<DraftResult>;

/// <summary>Choose what goes along.</summary>
public sealed record ChooseAttachmentsCommand(
    SubjectId Subject, Guid Id, bool SharesResume, IReadOnlyList<Guid> Documents)
    : ICommandMessage<DraftResult>;

/// <summary>Comment on something — with or without a marked passage.</summary>
public sealed record AddCommentCommand(SubjectId Subject, Guid Id, string? Text, string? Passage)
    : ICommandMessage<DraftResult>;

/// <summary>The model is to carry out the comments.</summary>
public sealed record ReviseCommand(SubjectId Subject, Guid Id) : ICommandMessage<DraftResult>;

/// <summary>Like this, and not otherwise.</summary>
public sealed record ApproveDraftCommand(SubjectId Subject, Guid Id) : ICommandMessage<DraftResult>;

/// <summary>Throw a draft away.</summary>
public sealed record DeleteDraftCommand(SubjectId Subject, Guid Id) : ICommandMessage<bool>;

/// <summary>Runs the drafts.</summary>
public sealed class DraftCommands(
    IDraftStore store,
    IJobLookup jobs,
    IAiAccessLookup ai,
    TimeProvider clock) :
    IRequestHandler<CreateDraftsCommand, IReadOnlyList<ApplicationDraft>>,
    IRequestHandler<WriteDraftCommand, DraftResult>,
    IRequestHandler<MyDraftsQuery, IReadOnlyList<ApplicationDraft>>,
    IRequestHandler<DraftQuery, ApplicationDraft?>,
    IRequestHandler<ChangeDraftCommand, DraftResult>,
    IRequestHandler<ChooseAttachmentsCommand, DraftResult>,
    IRequestHandler<AddCommentCommand, DraftResult>,
    IRequestHandler<ReviseCommand, DraftResult>,
    IRequestHandler<ApproveDraftCommand, DraftResult>,
    IRequestHandler<DeleteDraftCommand, bool>
{
    /// <summary>How many jobs a bulk action may carry.</summary>
    /// <remarks>
    /// Without a limit a caller builds any number of model calls with one
    /// request. Twenty is the number at which a review round is still a review
    /// round and not clicking through.
    /// </remarks>
    public const int MaxAtOnce = 20;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationDraft>> Handle(
        CreateDraftsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var drafts = new List<ApplicationDraft>();

        foreach (var id in request.Jobs.Distinct().Take(MaxAtOnce))
        {
            // Drafting twice for the same job costs a model call and confuses
            // the list. The open draft comes back instead.
            var open = await store.OpenOneAsync(request.Subject, id, cancellationToken);

            if (open is not null)
            {
                drafts.Add(open);
                continue;
            }

            var job = await jobs.GetAsync(id, cancellationToken);

            if (job is null)
            {
                continue;
            }

            var draft = ApplicationDraft.Begin(
                job.Id, job.Company, request.Subject, clock.GetUtcNow());

            await store.SaveAsync(draft, cancellationToken);
            drafts.Add(draft);
        }

        return drafts;
    }

    /// <inheritdoc />
    public async Task<DraftResult> Handle(
        WriteDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await store.GetAsync(request.Subject, request.Id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        // The model call runs behind the request (CoverLetterWorker). Here only:
        // fail at once without a provider, otherwise remember Generating.
        AiAccess access;

        try
        {
            access = await ai.GetAsync(request.Subject, cancellationToken);
        }
        catch (CoverLetterUnavailable error)
        {
            draft.Fail(error.Message, clock.GetUtcNow());
            await store.SaveAsync(draft, cancellationToken);

            return new DraftResult.NoProvider(error.Message);
        }

        if (!access.IsSetUp)
        {
            draft.Fail(CoverLetterUnavailable.NoProvider, clock.GetUtcNow());
            await store.SaveAsync(draft, cancellationToken);

            return new DraftResult.NoProvider(CoverLetterUnavailable.NoProvider);
        }

        try
        {
            // From Generating too: otherwise WritingStartedAt stays empty
            // (creating does not set it), and the interface counts from 0 at
            // every visit. Clear only when really writing anew — a running
            // writing must not lose its fragment.
            draft.StartWriting(
                clock.GetUtcNow(),
                clear: draft.State != DraftState.Generating);
        }
        catch (DraftStepNotAllowed error)
        {
            return new DraftResult.StateConflict(error.Message);
        }

        await store.SaveAsync(draft, cancellationToken);

        return new DraftResult.Done(draft);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApplicationDraft>> Handle(
        MyDraftsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.MineAsync(request.Subject, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ApplicationDraft?> Handle(
        DraftQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Subject, request.Id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        ChangeDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OnDraftAsync(
            request.Subject, request.Id,
            draft => draft.EditByAuthor(request.SubjectLine, request.Text, clock.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        ChooseAttachmentsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OnDraftAsync(
            request.Subject, request.Id,
            draft => draft.ChooseAttachments(
                request.SharesResume, request.Documents, clock.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        AddCommentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OnDraftAsync(
            request.Subject, request.Id,
            draft => draft.AddComment(request.Text, request.Passage, clock.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DraftResult> Handle(
        ReviseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await store.GetAsync(request.Subject, request.Id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        // NO CALL WITHOUT AN INSTRUCTION. Without an open comment this would be
        // the reflection stage from ADR-0024 under another name.
        if (!draft.HasOpenComments)
        {
            return new DraftResult.StateConflict(
                "There is no open comment — nothing to revise.");
        }

        AiAccess access;

        try
        {
            access = await ai.GetAsync(request.Subject, cancellationToken);
        }
        catch (CoverLetterUnavailable error)
        {
            return new DraftResult.NoProvider(error.Message);
        }

        if (!access.IsSetUp)
        {
            return new DraftResult.NoProvider(CoverLetterUnavailable.NoProvider);
        }

        try
        {
            draft.StartWriting(clock.GetUtcNow(), clear: false);
        }
        catch (DraftStepNotAllowed error)
        {
            return new DraftResult.StateConflict(error.Message);
        }

        await store.SaveAsync(draft, cancellationToken);

        return new DraftResult.Done(draft);
    }

    /// <inheritdoc />
    public Task<DraftResult> Handle(
        ApproveDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OnDraftAsync(
            request.Subject, request.Id,
            draft => draft.Approve(clock.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        DeleteDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var draft = await store.GetAsync(request.Subject, request.Id, cancellationToken);

        if (draft is null)
        {
            return false;
        }

        await store.DeleteAsync(request.Subject, request.Id, cancellationToken);

        return true;
    }

    /// <summary>A step on the draft, with always the same error handling.</summary>
    private async Task<DraftResult> OnDraftAsync(
        SubjectId subject, Guid id, Action<ApplicationDraft> step,
        CancellationToken cancellationToken)
    {
        var draft = await store.GetAsync(subject, id, cancellationToken);

        if (draft is null)
        {
            return new DraftResult.None();
        }

        try
        {
            step(draft);
        }
        catch (DraftStepNotAllowed error)
        {
            return new DraftResult.StateConflict(error.Message);
        }
        catch (CommentConflict error)
        {
            return new DraftResult.StateConflict(error.Message);
        }
        catch (InputError error)
        {
            return new DraftResult.InvalidInput(error.Message);
        }

        await store.SaveAsync(draft, cancellationToken);

        return new DraftResult.Done(draft);
    }
}
