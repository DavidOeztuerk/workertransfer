using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Requests;

namespace WorkerTransfer.Transfer.Application.Requests;

/// <summary>How a request turned out.</summary>
public abstract record ResumeRequestResult
{
    private ResumeRequestResult()
    {
    }

    /// <summary>It worked.</summary>
    public sealed record Erledigt(MarketRequest ResumeRequest) : ResumeRequestResult;

    /// <summary>
    /// Not visible, not present, not mine — from outside the same thing.
    /// </summary>
    /// <remarks>
    /// The difference would be especially expensive here: the mere existence of
    /// the statement gives something away.
    /// </remarks>
    public sealed record NichtSichtbar : ResumeRequestResult;

    /// <summary>This company has already asked.</summary>
    public sealed record SchonGefragt : ResumeRequestResult;

    /// <summary>The state does not fit.</summary>
    public sealed record StateConflict(string Grund) : ResumeRequestResult;
}

/// <summary>"May I see whether you are listening right now?"</summary>
/// <remarks>
/// The easier of the two questions — the harder one is the case itself.
/// Answering it costs nothing: whoever is <c>unavailable</c> and releases shows
/// exactly that, and nobody was disturbed.
/// </remarks>
public sealed record RequestMarketStatusCommand(SubjectId Subject, TenantId Company, SubjectId Frager)
    : ICommandMessage<ResumeRequestResult>;

/// <summary>The person answers.</summary>
public sealed record AnswerRequestCommand(Guid Id, SubjectId Subject, bool Erteilen)
    : ICommandMessage<ResumeRequestResult>;

/// <summary>The person takes the access back.</summary>
public sealed record RevokeAccessCommand(Guid Id, SubjectId Subject) : ICommandMessage<ResumeRequestResult>;

/// <summary>The requests about me, with the ledger's verdict beside them.</summary>
public sealed record MyRequestsQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<ResumeRequestView>>;

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
public sealed record ResumeRequestView(MarketRequest ResumeRequest, bool? Aktiv);

/// <summary>Asks, answers and revokes requests.</summary>
public sealed class RequestCommands(
    IRequestStore store,
    IConsentGate tor,
    IOutbox outbox,
    TimeProvider clock) :
    IRequestHandler<RequestMarketStatusCommand, ResumeRequestResult>,
    IRequestHandler<AnswerRequestCommand, ResumeRequestResult>,
    IRequestHandler<RevokeAccessCommand, ResumeRequestResult>,
    IRequestHandler<MyRequestsQuery, IReadOnlyList<ResumeRequestView>>,
    IRequestHandler<CompanyRequestsQuery, IReadOnlyList<MarketRequest>>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RequestMarketStatusCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The precondition is the PROFILE release, not the existence of a
        // market status. Checking both would be an oracle: "has already kept a
        // market status" is information about the person that nobody should be
        // able to ask for — and it would be especially telling here.
        if (!await tor.MaySeeProfileAsync(request.Subject, cancellationToken))
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        // Ask once. Without this rule a refusal would have no effect: whoever
        // may ask three times has not got a no but a delay. It holds after a
        // revocation too — that is a stronger statement than the refusal, not a
        // weaker one.
        if (await store.GetAsync(request.Subject, request.Company, cancellationToken) is not null)
        {
            return new ResumeRequestResult.SchonGefragt();
        }

        var requestRecord = MarketRequest.Oeffne(
            request.Subject, request.Company, request.Frager, clock.GetUtcNow());

        await store.SaveAsync(requestRecord, cancellationToken);

        // In THE SAME transaction (ADR-0025): if the request gets through, the
        // intent stands; if it is rolled back, the intent is gone too.
        await outbox.NoteAsync(
            request.Subject, NotificationKinds.Requested, cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }

    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        AnswerRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.Id, cancellationToken);

        // A foreign request id behaves like a foreign subject id: not present
        // and not mine are the same thing from outside.
        if (requestRecord is null || requestRecord.Subject != request.Subject)
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        var now = clock.GetUtcNow();

        try
        {
            if (request.Erteilen)
            {
                requestRecord.Erteile(request.Subject, now);
            }
            else
            {
                requestRecord.Lehne_ab(request.Subject, now);
            }
        }
        catch (AlreadyAnswered error)
        {
            return new ResumeRequestResult.StateConflict(error.Message);
        }
        catch (NotTheOneAsked)
        {
            return new ResumeRequestResult.NichtSichtbar();
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
        if (request.Erteilen)
        {
            await tor.GrantAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);
        }
        else
        {
            await tor.RevokeAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);
        }

        await store.SaveAsync(requestRecord, cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }

    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RevokeAccessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.Id, cancellationToken);

        if (requestRecord is null || requestRecord.Subject != request.Subject)
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        // The revocation takes effect in the ledger, not in the case.
        // `GRANTED` means "was granted once"; changing that state on a
        // revocation would rewrite the history, and whether the access holds
        // only the ledger says anyway. A running transfer case stays: it has a
        // door and a refusal of its own.
        await tor.RevokeAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeRequestView>> Handle(
        MyRequestsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecords = await store.ForPersonAsync(request.Subject, cancellationToken);

        // Ask only for granted requests: for PENDING and DECLINED the answer is
        // fixed, and every question costs a round trip.
        var erteilte = requestRecords.Where(a => a.State is RequestState.Granted).ToList();

        var urteile = await tor.MayEachSeeMarketAsync(
            [.. erteilte.Select(a => (a.Subject, a.Company))], cancellationToken);

        var aktivNachId = erteilte
            .Select((requestRecord, job) => (requestRecord.Id, Aktiv: urteile[job]))
            .ToDictionary(pair => pair.Id, pair => pair.Aktiv);

        return
        [
            .. requestRecords.Select(requestRecord => new ResumeRequestView(
                requestRecord,
                requestRecord.State is RequestState.Granted
                    ? aktivNachId.GetValueOrDefault(requestRecord.Id)
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
