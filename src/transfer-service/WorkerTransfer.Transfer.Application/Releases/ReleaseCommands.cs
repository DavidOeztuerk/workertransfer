using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Cases;
using WorkerTransfer.Transfer.Domain.Employers;
using WorkerTransfer.Transfer.Domain.Market;
using WorkerTransfer.Transfer.Domain.Releases;

namespace WorkerTransfer.Transfer.Application.Releases;

/// <summary>Who is looking.</summary>
public abstract record Viewer
{
    private Viewer()
    {
    }

    /// <summary>A human being acting for themselves.</summary>
    public sealed record Person(SubjectId Subject) : Viewer;

    /// <summary>Somebody acting for a company.</summary>
    public sealed record Company(TenantId Tenant) : Viewer;
}

/// <summary>A negotiation — or the possibility of one — as one party sees it.</summary>
/// <param name="CaseId">The transfer.</param>
/// <param name="Hiring">The company she would move to.</param>
/// <param name="Current">Her employer.</param>
/// <param name="Side">The viewer's side; <c>null</c> for the person.</param>
/// <param name="Negotiation"><c>null</c>: nothing asked yet, but the route is open.</param>
/// <param name="RegularFrom">
/// Her earliest regular start from her own notice period (H2), or <c>null</c>:
/// not stated. Both companies see it here, and only here, under her consent.
/// </param>
/// <param name="PersonName">Her civil name — only for her employer, who must know whom it is about.</param>
public sealed record ReleaseView(
    Guid CaseId,
    TenantId Hiring,
    TenantId Current,
    NegotiationSide? Side,
    ReleaseNegotiation? Negotiation,
    DateOnly? RegularFrom,
    string? PersonName = null);

/// <summary>How a step on a negotiation turned out.</summary>
public abstract record ReleaseResult
{
    private ReleaseResult()
    {
    }

    /// <summary>It worked.</summary>
    public sealed record Done(ReleaseView View) : ReleaseResult;

    /// <summary>Not present, not yours, or the route is closed — the same thing from outside.</summary>
    public sealed record Unknown : ReleaseResult;

    /// <summary>The input is fine, the state does not fit.</summary>
    public sealed record StateConflict(string Reason) : ReleaseResult;

    /// <summary>Something about the input is wrong.</summary>
    public sealed record InvalidInput(string Reason) : ReleaseResult;
}

/// <summary>The steps on a negotiation.</summary>
public enum ReleaseMove
{
    /// <summary>A company puts new terms on the table.</summary>
    Terms,

    /// <summary>The other company accepts them.</summary>
    Accept,

    /// <summary>The current employer says no.</summary>
    Refuse,

    /// <summary>The hiring company withdraws.</summary>
    Withdraw,

    /// <summary>The person agrees to what both companies agreed.</summary>
    Agree,

    /// <summary>The person says no.</summary>
    Decline
}

/// <summary>The negotiation of this transfer, as the viewer may see it.</summary>
public sealed record ReleaseForCaseQuery(Guid CaseId, Viewer Viewer) : IQueryMessage<ReleaseResult>;

/// <summary>The hiring company asks the current employer.</summary>
public sealed record AskReleaseCommand(Guid CaseId, TenantId Hiring, string? Month, long? FeeCents)
    : ICommandMessage<ReleaseResult>;

/// <summary>One step on a running negotiation.</summary>
public sealed record MoveReleaseCommand(
    Guid Id, Viewer Viewer, ReleaseMove Move, string? Month = null, long? FeeCents = null)
    : ICommandMessage<ReleaseResult>;

/// <summary>What other companies asked this employer about its staff.</summary>
public sealed record EmployerInquiriesQuery(TenantId Company) : IQueryMessage<IReadOnlyList<ReleaseView>>;

/// <summary>Runs the release negotiations (C3, D1, ADR-0050).</summary>
/// <remarks>
/// <para><strong>Every company read and step asks the ledger first</strong>,
/// synchronously and without a cache (ADR-0013): the person's consent to the
/// route is what lets two companies talk about her at all. Revoked, the
/// negotiation does not exist for them — 404, like everything not
/// released.</para>
///
/// <para><strong>Only the companies' steps are notified</strong>, to the person
/// and to the members of the other company — a kind and nothing else
/// (ADR-0025). The person's own steps are not, as in the transfer itself.</para>
/// </remarks>
public sealed class ReleaseCommands(
    IReleaseStore store,
    ITransferStore cases,
    IEmployerRouteStore routes,
    IEmployerSettingStore settings,
    INoticeStore notices,
    IMarketStore market,
    IConsentGate gate,
    IIdentityLookup identity,
    IOutbox outbox,
    TimeProvider clock) :
    IRequestHandler<ReleaseForCaseQuery, ReleaseResult>,
    IRequestHandler<AskReleaseCommand, ReleaseResult>,
    IRequestHandler<MoveReleaseCommand, ReleaseResult>,
    IRequestHandler<EmployerInquiriesQuery, IReadOnlyList<ReleaseView>>
{
    /// <inheritdoc />
    public async Task<ReleaseResult> Handle(ReleaseForCaseQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.CaseId, cancellationToken);
        var negotiation = await store.ForCaseAsync(request.CaseId, cancellationToken);

        switch (request.Viewer)
        {
            case Viewer.Person person when caseItem is not null && caseItem.Subject == person.Subject:
                // Hers, always — also after she closed the route: what was
                // negotiated about her she goes on reading.
                return negotiation is null
                    ? new ReleaseResult.Unknown()
                    : new ReleaseResult.Done(await ViewAsync(negotiation, side: null, cancellationToken));

            case Viewer.Company company when caseItem is not null && caseItem.Company == company.Tenant:
                if (negotiation is not null)
                {
                    return await gate.EmployerRouteOpenAsync(negotiation.Subject, negotiation.Current, cancellationToken)
                        ? new ReleaseResult.Done(await ViewAsync(negotiation, NegotiationSide.Hiring, cancellationToken))
                        : new ReleaseResult.Unknown();
                }

                return await OpenEmployerAsync(caseItem, cancellationToken) is { } employer
                    ? new ReleaseResult.Done(new ReleaseView(
                        caseItem.Id, caseItem.Company, employer, NegotiationSide.Hiring, null,
                        await RegularFromAsync(caseItem.Subject, cancellationToken)))
                    : new ReleaseResult.Unknown();

            default:
                return new ReleaseResult.Unknown();
        }
    }

    /// <inheritdoc />
    public async Task<ReleaseResult> Handle(AskReleaseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.CaseId, cancellationToken);

        if (caseItem is null || caseItem.Company != request.Hiring)
        {
            return new ReleaseResult.Unknown();
        }

        if (await store.ForCaseAsync(caseItem.Id, cancellationToken) is not null)
        {
            // web/src/features/work/api/releases.ts (RELEASE_CASES) matches this sentence.
            return new ReleaseResult.StateConflict("There is already a release negotiation for this transfer");
        }

        // A closed route answers exactly like a foreign transfer: whether she
        // works somewhere, and whether that employer lets itself be asked, is
        // nothing a company learns by trying.
        if (await OpenEmployerAsync(caseItem, cancellationToken) is not { } employer)
        {
            return new ReleaseResult.Unknown();
        }

        ReleaseNegotiation negotiation;

        try
        {
            negotiation = ReleaseNegotiation.Ask(caseItem, employer, request.Month, request.FeeCents, clock.GetUtcNow());
        }
        catch (CaseError error)
        {
            return ToResult(error);
        }

        await store.SaveAsync(negotiation, cancellationToken);
        await NotifyAsync(negotiation, NegotiationSide.Hiring, cancellationToken);

        return new ReleaseResult.Done(await ViewAsync(negotiation, NegotiationSide.Hiring, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ReleaseResult> Handle(MoveReleaseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await store.GetAsync(request.Id, cancellationToken) is not { } negotiation)
        {
            return new ReleaseResult.Unknown();
        }

        NegotiationSide? side = request.Viewer switch
        {
            Viewer.Company company when company.Tenant == negotiation.Hiring => NegotiationSide.Hiring,
            Viewer.Company company when company.Tenant == negotiation.Current => NegotiationSide.Current,
            _ => null
        };

        var isPerson = request.Viewer is Viewer.Person person && person.Subject == negotiation.Subject;

        if (side is null && !isPerson)
        {
            return new ReleaseResult.Unknown();
        }

        if (side is not null
            && !await gate.EmployerRouteOpenAsync(negotiation.Subject, negotiation.Current, cancellationToken))
        {
            return new ReleaseResult.Unknown();
        }

        var now = clock.GetUtcNow();

        try
        {
            switch (request.Move, side)
            {
                case (ReleaseMove.Terms, { } party):
                    negotiation.PutTerms(party, request.Month, request.FeeCents, now);
                    break;
                case (ReleaseMove.Accept, { } party):
                    negotiation.Accept(party, now);
                    break;
                case (ReleaseMove.Refuse, NegotiationSide.Current):
                    negotiation.Refuse(now);
                    break;
                case (ReleaseMove.Withdraw, NegotiationSide.Hiring):
                    negotiation.Withdraw(now);
                    break;
                case (ReleaseMove.Agree, null):
                    negotiation.Agree(((Viewer.Person)request.Viewer).Subject, now);
                    break;
                case (ReleaseMove.Decline, null):
                    negotiation.Decline(((Viewer.Person)request.Viewer).Subject, now);
                    break;
                default:
                    // web/src/features/work/api/releases.ts (RELEASE_CASES) matches this sentence.
                    return new ReleaseResult.StateConflict("This step belongs to another party");
            }
        }
        catch (CaseError error)
        {
            return ToResult(error);
        }

        await store.SaveAsync(negotiation, cancellationToken);

        if (side is { } mover)
        {
            await NotifyAsync(negotiation, mover, cancellationToken);
        }

        return new ReleaseResult.Done(await ViewAsync(negotiation, side, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReleaseView>> Handle(
        EmployerInquiriesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var all = await store.ForCurrentEmployerAsync(request.Company, cancellationToken);

        if (all.Count == 0)
        {
            return [];
        }

        // One question for the whole list, answered in its order (ADR-0030).
        var open = await gate.EmployerRoutesOpenAsync(
            [.. all.Select(negotiation => (negotiation.Subject, negotiation.Current))], cancellationToken);

        var views = new List<ReleaseView>();

        for (var index = 0; index < all.Count; index++)
        {
            if (!open[index])
            {
                continue;
            }

            views.Add(await ViewAsync(all[index], NegotiationSide.Current, cancellationToken));
        }

        return views;
    }

    /// <summary>
    /// Her employer, when every condition of the route holds — <c>null</c>
    /// otherwise, and for every reason the same.
    /// </summary>
    private async Task<TenantId?> OpenEmployerAsync(Domain.Cases.Transfer caseItem, CancellationToken cancellationToken)
    {
        if (!caseItem.IsRunning || !caseItem.NeedsRelease)
        {
            return null;
        }

        if (await routes.GetAsync(caseItem.Subject, cancellationToken) is not { } route
            || route.Employer == caseItem.Company)
        {
            return null;
        }

        if (await settings.GetAsync(route.Employer, cancellationToken) is not { AcceptsInquiries: true })
        {
            return null;
        }

        // The ledger last: it is the one that decides, and it is asked at
        // every call (ADR-0013).
        return await gate.EmployerRouteOpenAsync(caseItem.Subject, route.Employer, cancellationToken)
            ? route.Employer
            : null;
    }

    private async Task<ReleaseView> ViewAsync(
        ReleaseNegotiation negotiation, NegotiationSide? side, CancellationToken cancellationToken) =>
        new(
            negotiation.CaseId,
            negotiation.Hiring,
            negotiation.Current,
            side,
            negotiation,
            await RegularFromAsync(negotiation.Subject, cancellationToken),
            // The employer must know whom an inquiry is about; nobody else
            // gets the name from here. Asked only after the ledger said yes.
            side == NegotiationSide.Current
                ? await identity.PersonNameAsync(negotiation.Subject, cancellationToken)
                : null);

    // Only while she says she is employed, as everywhere the date is derived.
    private async Task<DateOnly?> RegularFromAsync(SubjectId subject, CancellationToken cancellationToken) =>
        await market.GetAsync(subject, cancellationToken) is { IsEmployed: true }
            ? (await notices.GetAsync(subject, cancellationToken))
                ?.EarliestStart(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            : null;

    /// <summary>The person and the OTHER company's members learn that something moved.</summary>
    /// <remarks>
    /// A kind and nothing else: no name, no company, no amount — the mail may
    /// land in an inbox the current employer reads. The person is left out of
    /// the members, in case she is one: one note is enough.
    /// </remarks>
    private async Task NotifyAsync(
        ReleaseNegotiation negotiation, NegotiationSide mover, CancellationToken cancellationToken)
    {
        await outbox.NoteAsync(negotiation.Subject, NotificationKinds.Moved, cancellationToken);

        var other = mover == NegotiationSide.Hiring ? negotiation.Current : negotiation.Hiring;

        foreach (var member in await identity.MembersAsync(other, cancellationToken))
        {
            if (member != negotiation.Subject)
            {
                await outbox.NoteAsync(member, NotificationKinds.Moved, cancellationToken);
            }
        }
    }

    private static ReleaseResult ToResult(CaseError error) => error switch
    {
        NotYours => new ReleaseResult.Unknown(),
        NegotiationNotNow or TransitionNotAllowed => new ReleaseResult.StateConflict(error.Message),
        _ => new ReleaseResult.InvalidInput(error.Message)
    };
}
