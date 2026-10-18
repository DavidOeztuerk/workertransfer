using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Jobs.Application.Messages;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Domain.Sources;

namespace WorkerTransfer.Jobs.Application.Sources;

/// <summary>How setting up a source turned out.</summary>
public abstract record SourceResult
{
    private SourceResult()
    {
    }

    /// <summary>Set up (or changed).</summary>
    public sealed record Done(JobSource Source) : SourceResult;

    /// <summary>No such source for this company.</summary>
    public sealed record Unknown : SourceResult;

    /// <summary>The input does not keep the rules.</summary>
    public sealed record Invalid(string Reason) : SourceResult;
}

/// <summary>What a fetch came to.</summary>
public sealed record FetchOutcome(JobSource Source, int Added, int Changed, int Closed);

/// <summary>This company's sources.</summary>
public sealed record SourcesQuery(TenantId Company) : IQueryMessage<IReadOnlyList<JobSource>>;

/// <summary>
/// Checks a source before it is saved: the rules, and for a feed the company's
/// proven domain. A query — asking identity-service is a network call, and no
/// transaction stays open for it.
/// </summary>
public sealed record CheckSourceQuery(TenantId Company, SourceKind Kind, string? Key, PublishMode Mode)
    : IQueryMessage<SourceResult>;

/// <summary>Saves a checked source.</summary>
public sealed record SaveSourceCommand(JobSource Source) : ICommandMessage<JobSource>;

/// <summary>Switches a source between draft and direct.</summary>
public sealed record SetSourceModeCommand(TenantId Company, Guid Id, PublishMode Mode) : ICommandMessage<SourceResult>;

/// <summary>Removes a source; its adverts stay and become the company's own.</summary>
public sealed record DeleteSourceCommand(TenantId Company, Guid Id) : ICommandMessage<bool>;

/// <summary>Reads a source. <c>Company</c> is null for the daily run.</summary>
public sealed record ReadSourceQuery(Guid Id, TenantId? Company) : IQueryMessage<(JobSource Source, SourceRead Read)?>;

/// <summary>Brings the company's adverts in line with what was read.</summary>
public sealed record ApplySourceCommand(Guid Id, SourceRead Read) : ICommandMessage<FetchOutcome?>;

/// <summary>Runs the sources (ADR-0052).</summary>
/// <remarks>
/// <para><strong>Reading and writing are two steps.</strong> Reading talks to
/// the ATS and to the company's site and takes seconds; writing touches the
/// database. Kept apart, no transaction waits for the network.</para>
///
/// <para><strong>Nothing is imported from an unproven board.</strong> The
/// read that finds the code proves it and imports in the same step; until then
/// a read changes no advert. The daily run reads only proven sources — an
/// unproven one is read on a click, never on a timer.</para>
/// </remarks>
public sealed class SourceCommands(
    IJobSourceStore sources,
    IJobStore jobs,
    ISourceReader reader,
    ICompanyDomains domains,
    IKnownDomains known,
    TimeProvider clock) :
    IRequestHandler<SourcesQuery, IReadOnlyList<JobSource>>,
    IRequestHandler<CheckSourceQuery, SourceResult>,
    IRequestHandler<SaveSourceCommand, JobSource>,
    IRequestHandler<SetSourceModeCommand, SourceResult>,
    IRequestHandler<DeleteSourceCommand, bool>,
    IRequestHandler<ReadSourceQuery, (JobSource Source, SourceRead Read)?>,
    IRequestHandler<ApplySourceCommand, FetchOutcome?>
{
    /// <summary>The most adverts one source brings in — a board larger than this is cut, and says so.</summary>
    public const int MaxAdverts = 500;

    /// <inheritdoc />
    public Task<IReadOnlyList<JobSource>> Handle(SourcesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return sources.ForCompanyAsync(request.Company, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SourceResult> Handle(CheckSourceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (request.Kind != SourceKind.Feed)
            {
                return new SourceResult.Done(
                    JobSource.Board(request.Company, request.Kind, request.Key, request.Mode, clock.GetUtcNow()));
            }

            var domain = await domains.DomainAsync(request.Company, cancellationToken) ?? string.Empty;
            var feed = JobSource.Feed(request.Company, request.Key, domain, request.Mode, clock.GetUtcNow());

            // The egress boundary learns the domain before anything is fetched
            // from it — and only a domain this company has proven.
            known.Remember(feed.Domain);
            return new SourceResult.Done(feed);
        }
        catch (SourceError error)
        {
            return new SourceResult.Invalid(error.Message);
        }
    }

    /// <inheritdoc />
    public async Task<JobSource> Handle(SaveSourceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await sources.SaveAsync(request.Source, cancellationToken);
        return request.Source;
    }

    /// <inheritdoc />
    public async Task<SourceResult> Handle(SetSourceModeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await sources.GetAsync(request.Id, cancellationToken) is not { } source || source.Company != request.Company)
        {
            return new SourceResult.Unknown();
        }

        source.SetMode(request.Mode);
        await sources.SaveAsync(source, cancellationToken);
        return new SourceResult.Done(source);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(DeleteSourceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await sources.GetAsync(request.Id, cancellationToken) is not { } source || source.Company != request.Company)
        {
            return false;
        }

        // The adverts stay — they are the company's, and people may have
        // applied. They stop mirroring and can be edited here from now on.
        foreach (var job in await jobs.FromSourceAsync(source.Id, cancellationToken))
        {
            job.Detach();
            await jobs.SaveAsync(job, cancellationToken);
        }

        await sources.DeleteAsync(source.Id, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<(JobSource Source, SourceRead Read)?> Handle(ReadSourceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await sources.GetAsync(request.Id, cancellationToken) is not { } source
            || (request.Company is { } company && source.Company != company))
        {
            return null;
        }

        if (source.Kind == SourceKind.Feed)
        {
            known.Remember(source.Domain);
        }

        return (source, await reader.ReadAsync(source, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<FetchOutcome?> Handle(ApplySourceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await sources.GetAsync(request.Id, cancellationToken) is not { } source)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        if (request.Read is SourceRead.Failed failed)
        {
            // A failed read changes no advert: a board that does not answer
            // today has not taken its adverts down.
            source.Fetched(now, failed.Outcome);
            await sources.SaveAsync(source, cancellationToken);
            return new FetchOutcome(source, 0, 0, 0);
        }

        var read = ((SourceRead.Adverts)request.Read).Items;

        if (!source.ProveBy(read, now))
        {
            // web/src/features/company/api/sources.ts names this word.
            source.Fetched(now, "code_missing");
            await sources.SaveAsync(source, cancellationToken);
            return new FetchOutcome(source, 0, 0, 0);
        }

        var adverts = read
            .Where(advert => advert.ExternalId.Length is > 0 and <= JobOrigin.MaxIdLength)
            .DistinctBy(advert => advert.ExternalId)
            .ToList();

        var cut = adverts.Count > MaxAdverts;
        adverts = [.. adverts.Take(MaxAdverts)];

        var existing = (await jobs.FromSourceAsync(source.Id, cancellationToken))
            .Where(job => job.Origin is not null)
            .ToDictionary(job => job.Origin!.ExternalId);

        int added = 0, changed = 0, closed = 0;

        foreach (var advert in adverts)
        {
            try
            {
                if (existing.TryGetValue(advert.ExternalId, out var job))
                {
                    var before = job.UpdatedAt;
                    job.Mirror(advert, now);

                    if (job.UpdatedAt != before)
                    {
                        changed++;
                        await jobs.SaveAsync(job, cancellationToken);
                    }

                    continue;
                }

                var fresh = Job.Import(source.Company, JobOrigin.Of(source.Id, advert)!, advert, now);

                if (source.Mode == PublishMode.Direct)
                {
                    fresh.Publish(now);
                }

                await jobs.SaveAsync(fresh, cancellationToken);
                added++;
            }
            catch (InputError)
            {
                // An advert without a title cannot stand here; the others can.
            }
        }

        // Gone at the source: a draft goes, a published advert closes —
        // applications to it stay. Closed stays closed.
        var listed = adverts.Select(advert => advert.ExternalId).ToHashSet();

        foreach (var (externalId, job) in existing)
        {
            if (listed.Contains(externalId) || cut)
            {
                continue;
            }

            if (job.State == JobState.Draft)
            {
                await jobs.DeleteAsync(job.Id, cancellationToken);
                closed++;
            }
            else if (job.State == JobState.Published)
            {
                job.Close(now);
                await jobs.SaveAsync(job, cancellationToken);
                closed++;
            }
        }

        source.Fetched(now, cut ? "cut" : "ok");
        await sources.SaveAsync(source, cancellationToken);

        return new FetchOutcome(source, added, changed, closed);
    }
}
