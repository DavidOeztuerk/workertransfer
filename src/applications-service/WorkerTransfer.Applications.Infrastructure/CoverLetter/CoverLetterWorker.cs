using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerTransfer.Applications.Application.Drafts;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Security;

namespace WorkerTransfer.Applications.Infrastructure.CoverLetter;

/// <summary>
/// Writes in the background. The HTTP request returns as soon as the job is
/// accepted — otherwise the browser hangs for 133 seconds (measured) and a page
/// change aborts the writing.
/// </summary>
public sealed class CoverLetterWorker(
    IServiceScopeFactory scopes,
    ILogger<CoverLetterWorker> log) : BackgroundService, ICoverLetterQueue
{
    private readonly Channel<CoverLetterJob> _channel =
        Channel.CreateUnbounded<CoverLetterJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Guid, byte> _running = new();

    /// <inheritdoc />
    public bool Enqueue(CoverLetterJob letterJob)
    {
        ArgumentNullException.ThrowIfNull(letterJob);

        if (!_running.TryAdd(letterJob.DraftId, 0))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(letterJob))
        {
            log.LogInformation(
                "Cover letter {DraftId} queued ({Kind})",
                letterJob.DraftId, letterJob.Kind);
            return true;
        }

        _running.TryRemove(letterJob.DraftId, out _);
        return false;
    }

    /// <inheritdoc />
    public bool IsRunning(Guid draftId) => _running.ContainsKey(draftId);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var letterJob in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await PerformAsync(letterJob, stoppingToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                log.LogWarning(
                    error,
                    "Cover-letter job {DraftId} failed",
                    letterJob.DraftId);
            }
            finally
            {
                _running.TryRemove(letterJob.DraftId, out _);
            }
        }
    }

    private async Task PerformAsync(
        CoverLetterJob letterJob, CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope();
        using var _ = HttpCallerToken.Use(letterJob.Bearer);

        var drafts = scope.ServiceProvider.GetRequiredService<IDraftStore>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var writer = scope.ServiceProvider.GetRequiredService<ICoverLetterWriter>();
        var ai = scope.ServiceProvider.GetRequiredService<IAiAccessLookup>();
        var jobs = scope.ServiceProvider.GetRequiredService<IJobLookup>();
        var applicant = scope.ServiceProvider.GetRequiredService<IApplicantLookup>();
        var companies = scope.ServiceProvider.GetRequiredService<ICompanyNameLookup>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        // SaveAsync alone does not commit — only the command scope does that.
        // The worker is no command. Without this scope the letter stays empty
        // although the model is done (measured: 600–900 characters, state still
        // Generating, GET queues it anew every three minutes).
        Task Persist(ApplicationDraft state, CancellationToken token) =>
            unitOfWork.InTransactionAsync(async ct =>
            {
                await drafts.SaveAsync(state, ct);
                return 0;
            }, token);

        var draft = await WaitForStateAsync(drafts, letterJob, stoppingToken);

        if (draft is null)
        {
            return;
        }

        if (draft.WritingStartedAt is null)
        {
            draft.StartWriting(clock.GetUtcNow(), clear: false);
            await Persist(draft, stoppingToken);
        }

        AiAccess access;

        try
        {
            access = await ai.GetAsync(letterJob.Subject, stoppingToken);
        }
        catch (CoverLetterUnavailable error)
        {
            await FailAsync(Persist, draft, error.Message, clock, stoppingToken);
            return;
        }

        if (!access.IsSetUp)
        {
            await FailAsync(
                Persist, draft, CoverLetterUnavailable.NoProvider, clock, stoppingToken);
            return;
        }

        var job = await jobs.GetAsync(draft.Job, stoppingToken);

        if (job is null)
        {
            await FailAsync(
                Persist, draft, "The job is no longer open.", clock, stoppingToken);
            return;
        }

        CoverLetterContext context;

        try
        {
            var own = await applicant.GetAsync(stoppingToken);
            var company = await companies.NameAsync(job.Company, stoppingToken);

            context = new CoverLetterContext(
                job.Title, company, job.Location, job.Description, job.Skills,
                own.Name, own.Headline, own.Text, own.Skills,
                own.Career, own.Language);
        }
        catch (ApplicantLookupSilent error)
        {
            await FailAsync(Persist, draft, error.Message, clock, stoppingToken);
            return;
        }

        var lastLength = 0;
        var lastAt = clock.GetUtcNow();

        async Task Progress(string raw)
        {
            var now = clock.GetUtcNow();

            // Always write the first token — otherwise the letter stays empty
            // until forty characters or 400 ms have gathered, and a slow model
            // looks as if it were not writing.
            if (lastLength > 0
                && raw.Length - lastLength < 40
                && now - lastAt < TimeSpan.FromMilliseconds(400))
            {
                return;
            }

            if (lastLength == 0)
            {
                log.LogInformation(
                    "Cover letter {DraftId}: first fragment ({Characters} characters)",
                    letterJob.DraftId, raw.Length);
            }

            lastLength = raw.Length;
            lastAt = now;
            var (subjectLine, text) = CoverLetterFormat.Parse(raw);
            var state = await drafts.GetAsync(letterJob.Subject, letterJob.DraftId, stoppingToken);

            if (state is null || state.State != DraftState.Generating)
            {
                return;
            }

            try
            {
                state.TakeFragment(subjectLine, text, now);
                await Persist(state, stoppingToken);
            }
            catch (Exception saveError) when (saveError is not OperationCanceledException)
            {
                // A storage error must not kill the stream — otherwise the model
                // dies after the first character, and GET queues it anew.
                log.LogWarning(
                    saveError,
                    "Cover letter {DraftId}: fragment not saved",
                    letterJob.DraftId);
            }
        }

        try
        {
            string output;

            log.LogInformation(
                "Cover letter {DraftId}: model {Model} via {Provider}",
                letterJob.DraftId, access.Model, access.Provider);

            if (letterJob.Kind == CoverLetterJobKind.Revise)
            {
                // The line handed to the model stays German, like the prompt it
                // extends (CoverLetterContext.Rules).
                var instructions = draft.OpenComments
                    .Select(entry => entry.Passage.Length > 0
                        ? $"Zur markierten Stelle [{entry.Passage}]: {entry.Text}"
                        : entry.Text)
                    .ToArray();

                output = await writer.ReviseAsync(
                    access, context, draft.SubjectLine, draft.Text, instructions,
                    Progress, stoppingToken);

                var fresh = await drafts.GetAsync(letterJob.Subject, letterJob.DraftId, stoppingToken);

                if (fresh is null)
                {
                    return;
                }

                var (subjectLine, text) = CoverLetterFormat.Parse(output);
                fresh.Revise(subjectLine, text, clock.GetUtcNow());
                await Persist(fresh, stoppingToken);
                return;
            }

            output = await writer.WriteAsync(
                access, context, Progress, stoppingToken);

            log.LogInformation(
                "Cover letter {DraftId}: model done ({Characters} characters)",
                letterJob.DraftId, output.Length);

            var afterwards = await drafts.GetAsync(letterJob.Subject, letterJob.DraftId, stoppingToken);

            if (afterwards is null)
            {
                return;
            }

            var finished = CoverLetterFormat.Parse(output);
            afterwards.AcceptText(finished.SubjectLine, finished.Text, clock.GetUtcNow());
            await Persist(afterwards, stoppingToken);
        }
        catch (CoverLetterUnavailable error)
        {
            log.LogWarning(
                "Cover letter {DraftId} aborted: {Reason}",
                letterJob.DraftId, error.Message);

            var state = await drafts.GetAsync(letterJob.Subject, letterJob.DraftId, stoppingToken);

            if (state is null)
            {
                return;
            }

            if (state.Text.Length > 0)
            {
                state.AcceptText(state.SubjectLine, state.Text, clock.GetUtcNow());
            }
            else
            {
                state.Fail(error.Message, clock.GetUtcNow());
            }

            await Persist(state, stoppingToken);
        }
    }

    private static async Task<ApplicationDraft?> WaitForStateAsync(
        IDraftStore store,
        CoverLetterJob letterJob,
        CancellationToken stoppingToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var draft = await store.GetAsync(
                letterJob.Subject, letterJob.DraftId, stoppingToken);

            if (draft is not null && draft.State == DraftState.Generating)
            {
                return draft;
            }

            await Task.Delay(50, stoppingToken);
        }

        return await store.GetAsync(letterJob.Subject, letterJob.DraftId, stoppingToken);
    }

    private static async Task FailAsync(
        Func<ApplicationDraft, CancellationToken, Task> persist,
        ApplicationDraft draft,
        string reason,
        TimeProvider clock,
        CancellationToken stoppingToken)
    {
        draft.Fail(reason, clock.GetUtcNow());
        await persist(draft, stoppingToken);
    }
}
