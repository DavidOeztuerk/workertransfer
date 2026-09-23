using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Requests;

namespace WorkerTransfer.Transfer.Application.Requests;

/// <summary>How a request turned out.</summary>
public abstract record MarketRequestResult
{
    private MarketRequestResult()
    {
    }

    /// <summary>It worked.</summary>
    public sealed record Done(MarketRequest MarketRequest) : MarketRequestResult;

    /// <summary>
    /// Not visible, not present, not mine — from outside the same thing.
    /// </summary>
    /// <remarks>
    /// The difference would be especially expensive here: the mere existence of
    /// the statement gives something away.
    /// </remarks>
    public sealed record NotVisible : MarketRequestResult;

    /// <summary>This company has already asked.</summary>
    public sealed record AlreadyAsked : MarketRequestResult;

    /// <summary>The state does not fit.</summary>
    public sealed record StateConflict(string Reason) : MarketRequestResult;
}

/// <summary>"May I see whether you are listening right now?"</summary>
/// <remarks>
/// The easier of the two questions — the harder one is the case itself.
/// Answering it costs nothing: whoever is <c>unavailable</c> and releases shows
/// exactly that, and nobody was disturbed.
/// </remarks>
public sealed record RequestMarketStatusCommand(SubjectId Subject, TenantId Company, SubjectId RequestedBy)
    : ICommandMessage<MarketRequestResult>;

/// <summary>The person answers.</summary>
public sealed record AnswerRequestCommand(Guid Id, SubjectId Subject, bool Grant)
    : ICommandMessage<MarketRequestResult>;

/// <summary>The person takes the access back.</summary>
public sealed record RevokeAccessCommand(Guid Id, SubjectId Subject) : ICommandMessage<MarketRequestResult>;

/// <summary>The requests about me, with the ledger's verdict beside them.</summary>
public sealed record MyRequestsQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<MarketRequestView>>;

/// <summary>This company's requests.</summary>
/// <remarks>
/// Refused ones stay visible too. Otherwise "refused" and "never asked" would
/// look the same — and then somebody asks again, in good faith.
/// </remarks>
public sealed record CompanyRequestsQuery(TenantId Company) : IQueryMessage<IReadOnlyList<MarketRequest>>;

/// <summary>A request and whether the access holds right now.</summary>
/// <remarks>
/// <c>Active</c> comes fresh from the ledger and may differ from the state:
/// after a revocation <c>GRANTED</c> stays standing, <c>Active</c> falls to
/// false. That is exactly why the permission does not stand in the case.
/// </remarks>
public sealed record MarketRequestView(MarketRequest MarketRequest, bool? Active);

/// <summary>Asks, answers and revokes requests.</summary>
public sealed class RequestCommands(
    IRequestStore store,
    IConsentGate gate,
    IOutbox outbox,
    TimeProvider clock) :
    IRequestHandler<RequestMarketStatusCommand, MarketRequestResult>,
    IRequestHandler<AnswerRequestCommand, MarketRequestResult>,
    IRequestHandler<RevokeAccessCommand, MarketRequestResult>,
    IRequestHandler<MyRequestsQuery, IReadOnlyList<MarketRequestView>>,
    IRequestHandler<CompanyRequestsQuery, IReadOnlyList<MarketRequest>>
{
    /// <inheritdoc />
    public async Task<MarketRequestResult> Handle(
        RequestMarketStatusCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The precondition is the PROFILE release, not the existence of a
        // market status. Checking both would be an oracle: "has already kept a
        // market status" is information about the person that nobody should be
        // able to ask for — and it would be especially telling here.
        if (!await gate.MaySeeProfileAsync(request.Subject, cancellationToken))
        {
            return new MarketRequestResult.NotVisible();
        }

        // Ask once. Without this rule a refusal would have no effect: whoever
        // may ask three times has not got a no but a delay. It holds after a
        // revocation too — that is a stronger statement than the refusal, not a
        // weaker one.
        if (await store.GetAsync(request.Subject, request.Company, cancellationToken) is not null)
        {
            return new MarketRequestResult.AlreadyAsked();
        }

        var requestRecord = MarketRequest.Open(
            request.Subject, request.Company, request.RequestedBy, clock.GetUtcNow());

        await store.SaveAsync(requestRecord, cancellationToken);

        // In THE SAME transaction (ADR-0025): if the request gets through, the
        // intent stands; if it is rolled back, the intent is gone too.
        await outbox.NoteAsync(
            request.Subject, NotificationKinds.Requested, cancellationToken);

        return new MarketRequestResult.Done(requestRecord);
    }

    /// <inheritdoc />
    public async Task<MarketRequestResult> Handle(
        AnswerRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.Id, cancellationToken);

        // A foreign request id behaves like a foreign subject id: not present
        // and not mine are the same thing from outside.
        if (requestRecord is null || requestRecord.Subject != request.Subject)
        {
            return new MarketRequestResult.NotVisible();
        }

        var now = clock.GetUtcNow();

        try
        {
            if (request.Grant)
            {
                requestRecord.Grant(request.Subject, now);
            }
            else
            {
                requestRecord.Decline(request.Subject, now);
            }
        }
        catch (AlreadyAnswered error)
        {
            return new MarketRequestResult.StateConflict(error.Message);
        }
        catch (NotTheOneAsked)
        {
            return new MarketRequestResult.NotVisible();
        }

        // The ledger first, the case second: if the ledger fails,
        // ConsentGateSilent flies through and the transaction is never
        // committed.
        //
        // The refusal revokes too. If the ledger call succeeds and the commit
        // then fails, a permission would otherwise exist without a visible case
        // — the one way in which this system could fail OPEN to the outside.
        // And here it would be most expensive: the permission says "this person
        // is listening".
        if (request.Grant)
        {
            await gate.GrantAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);
        }
        else
        {
            await gate.RevokeAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);
        }

        await store.SaveAsync(requestRecord, cancellationToken);

        return new MarketRequestResult.Done(requestRecord);
    }

    /// <inheritdoc />
    public async Task<MarketRequestResult> Handle(
        RevokeAccessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.Id, cancellationToken);

        if (requestRecord is null || requestRecord.Subject != request.Subject)
        {
            return new MarketRequestResult.NotVisible();
        }

        // The revocation takes effect in the ledger, not in the case.
        // `GRANTED` means "was granted once"; changing that state on a
        // revocation would rewrite the history, and whether the access holds
        // only the ledger says anyway. A running transfer case stays: it has a
        // door and a refusal of its own.
        await gate.RevokeAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);

        return new MarketRequestResult.Done(requestRecord);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketRequestView>> Handle(
        MyRequestsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecords = await store.ForPersonAsync(request.Subject, cancellationToken);

        // Ask only for granted requests: for PENDING and DECLINED the answer is
        // fixed, and every question costs a round trip.
        var granted = requestRecords.Where(a => a.State is RequestState.Granted).ToList();

        var verdicts = await gate.MayEachSeeMarketAsync(
            [.. granted.Select(a => (a.Subject, a.Company))], cancellationToken);

        var activeById = granted
            .Select((requestRecord, index) => (requestRecord.Id, Active: verdicts[index]))
            .ToDictionary(pair => pair.Id, pair => pair.Active);

        return
        [
            .. requestRecords.Select(requestRecord => new MarketRequestView(
                requestRecord,
                requestRecord.State is RequestState.Granted
                    ? activeById.GetValueOrDefault(requestRecord.Id)
                    : false))
        ];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MarketRequest>> Handle(
        CompanyRequestsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForCompanyAsync(request.Company, cancellationToken);
    }
}
