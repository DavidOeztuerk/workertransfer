using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Market;

namespace WorkerTransfer.Transfer.Application.Market;

/// <summary>Write one's own market status.</summary>
public sealed record SaveMarketStatusCommand(
    SubjectId Subject,
    Availability Availability,
    bool IsEmployed,
    string Remark) : ICommandMessage<MarketStatus>;

/// <summary>One's own market status.</summary>
/// <remarks>
/// Never <c>null</c>. Unlike the profile: there "none yet" is an empty form,
/// here "said nothing" is a real state with a meaning — not available. A
/// <c>null</c> would force the interface to invent a default, and the danger is
/// that it invents the wrong one.
/// </remarks>
public sealed record MyMarketStatusQuery(SubjectId Subject) : IQueryMessage<MarketStatus>;

/// <summary>Another person's market status — if it is released.</summary>
public sealed record VisibleMarketStatusQuery(SubjectId Subject, TenantId Company)
    : IQueryMessage<MarketStatus?>;

/// <summary>Writes and reads the market status.</summary>
public sealed class MarketCommands(
    IMarketStore store,
    IConsentGate gate,
    TimeProvider clock) :
    IRequestHandler<SaveMarketStatusCommand, MarketStatus>,
    IRequestHandler<MyMarketStatusQuery, MarketStatus>,
    IRequestHandler<VisibleMarketStatusQuery, MarketStatus?>
{
    /// <inheritdoc />
    public async Task<MarketStatus> Handle(
        SaveMarketStatusCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();
        var existing = await store.GetAsync(request.Subject, cancellationToken);
        MarketStatus status;

        if (existing is null)
        {
            status = MarketStatus.Create(
                request.Subject, request.Availability, request.IsEmployed,
                request.Remark, now);
        }
        else
        {
            existing.Change(
                request.Availability, request.IsEmployed, request.Remark, now);

            status = existing;
        }

        await store.SaveAsync(status, cancellationToken);

        return status;
    }

    /// <inheritdoc />
    public async Task<MarketStatus> Handle(
        MyMarketStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await store.GetAsync(request.Subject, cancellationToken)
               ?? MarketStatus.Default(request.Subject, clock.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<MarketStatus?> Handle(
        VisibleMarketStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await store.GetAsync(request.Subject, cancellationToken);

        if (status is null)
        {
            // No ledger call for something that does not exist: an unnecessary
            // round trip, and it would report guessed ids to the ledger.
            return null;
        }

        // ConsentGateSilent deliberately flies through: the endpoint turns it
        // into 503. Not present and not released, on the other hand, are the
        // same thing from outside — and that counts double here, because the
        // mere existence of the statement "this person is listening" can do
        // harm.
        return await gate.MaySeeMarketAsync(request.Subject, request.Company, cancellationToken)
            ? status
            : null;
    }
}
