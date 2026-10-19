using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Jobs.Application.Sources;
using WorkerTransfer.Jobs.Domain.Sources;

namespace WorkerTransfer.Jobs.Infrastructure.Sources;

/// <summary>Teaches the egress boundary the domains with a source, at startup (ADR-0052).</summary>
/// <remarks>Started after the schema migration — <c>Program.cs</c> migrates before it runs the host.</remarks>
public sealed class KnownDomainsLoader(IServiceScopeFactory scopes, KnownDomains known) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IJobSourceStore>();

        foreach (var domain in await sources.DomainsAsync(cancellationToken))
        {
            known.Remember(domain);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Fetches every source once a day — adverts, never people (ADR-0052).</summary>
/// <remarks>
/// <para>It wakes every <see cref="SourceSettings.Every"/> and fetches the
/// sources not fetched for <see cref="SourceSettings.Due"/>: a restart does not
/// skip a day, and a stack started for an hour fetches nothing it fetched an
/// hour ago.</para>
///
/// <para>Logged are counts and outcome words, never what a board says.</para>
/// </remarks>
public sealed class SourceSchedule(
    IServiceScopeFactory scopes,
    IOptions<SourceSettings> settings,
    TimeProvider clock,
    ILogger<SourceSchedule> log) : BackgroundService
{
    private readonly SourceSettings _settings = settings.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.Every <= TimeSpan.Zero)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_settings.Every, clock, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                // One bad round must not end the schedule.
                log.LogWarning("A source round failed ({Kind})", error.GetType().Name);
            }
        }
    }

    /// <summary>One round over the due sources.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> due;

        using (var scope = scopes.CreateScope())
        {
            var sources = scope.ServiceProvider.GetRequiredService<IJobSourceStore>();
            due = [.. (await sources.DueAsync(clock.GetUtcNow() - _settings.Due, cancellationToken)).Select(source => source.Id)];
        }

        foreach (var id in due)
        {
            using var scope = scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            if (await mediator.Send(new ReadSourceQuery(id, null), cancellationToken) is not { } read)
            {
                continue;
            }

            var outcome = await mediator.Send(new ApplySourceCommand(id, read.Read), cancellationToken);

            log.LogInformation(
                "Source fetched: {Outcome}, {Added} added, {Changed} changed, {Closed} closed",
                outcome?.Source.Outcome, outcome?.Added, outcome?.Changed, outcome?.Closed);
        }
    }
}
