using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Market;
using WorkerTransfer.Transfer.Domain.Cases;

namespace WorkerTransfer.Transfer.Application.Cases;

/// <summary>How a command on a case turned out.</summary>
public abstract record CaseResult
{
    private CaseResult()
    {
    }

    /// <summary>It worked.</summary>
    public sealed record Erledigt(Transfer.Domain.Cases.Transfer Case) : CaseResult;

    /// <summary>Not present OR not yours — from outside the same thing.</summary>
    public sealed record Unbekannt : CaseResult;

    /// <summary>
    /// No status, no release, or <c>unavailable</c> — all the same to the
    /// outside.
    /// </summary>
    /// <remarks>
    /// Otherwise the endpoint would be an oracle about who is on the platform
    /// and who is listening right now.
    /// </remarks>
    public sealed record NichtAnsprechbar : CaseResult;

    /// <summary>One is already running.</summary>
    public sealed record AlreadyRunning : CaseResult;

    /// <summary>The input is fine, the state does not fit.</summary>
    public sealed record StateConflict(string Grund) : CaseResult;

    /// <summary>Something about the input is wrong.</summary>
    public sealed record Eingabe(string Grund) : CaseResult;
}

/// <summary>Which move the person makes.</summary>
public enum Personenzug
{
    /// <summary>Agree to a conversation.</summary>
    AcceptConversationCommand,

    /// <summary>Accept the offer.</summary>
    AcceptOfferCommand,

    /// <summary>Confirm the employer's release.</summary>
    ConfirmReleaseCommand,

    /// <summary>Decline.</summary>
    Absagen
}

/// <summary>Which move the company makes.</summary>
public enum CompanySwitch
{
    /// <summary>Complete.</summary>
    Complete,

    /// <summary>Withdraw.</summary>
    WithdrawCommand
}

/// <summary>A company shows interest.</summary>
public sealed record ShowInterestCommand(SubjectId Subject, TenantId Company, string Nachricht)
    : ICommandMessage<CaseResult>;

/// <summary>The person moves their case.</summary>
public sealed record PersonMoveCommand(Guid Id, SubjectId Subject, Personenzug Zug)
    : ICommandMessage<CaseResult>;

/// <summary>The company moves the case.</summary>
public sealed record CompanySwitchCommand(Guid Id, TenantId Company, CompanySwitch Zug)
    : ICommandMessage<CaseResult>;

/// <summary>The company makes an offer.</summary>
public sealed record MakeOfferCommand(
    Guid Id, TenantId Company, string Text, string? Beginn, long? FeeCents)
    : ICommandMessage<CaseResult>;

/// <summary>This person's cases.</summary>
public sealed record MyCasesQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<Transfer.Domain.Cases.Transfer>>;

/// <summary>This company's cases.</summary>
public sealed record CompanyCasesQuery(TenantId Company)
    : IQueryMessage<IReadOnlyList<Transfer.Domain.Cases.Transfer>>;

/// <summary>Runs the cases.</summary>
public sealed class CaseCommands(
    ITransferStore store,
    IMarketStore market,
    IConsentGate tor,
    IOutbox outbox,
    TimeProvider clock) :
    IRequestHandler<ShowInterestCommand, CaseResult>,
    IRequestHandler<PersonMoveCommand, CaseResult>,
    IRequestHandler<CompanySwitchCommand, CaseResult>,
    IRequestHandler<MakeOfferCommand, CaseResult>,
    IRequestHandler<MyCasesQuery, IReadOnlyList<Transfer.Domain.Cases.Transfer>>,
    IRequestHandler<CompanyCasesQuery, IReadOnlyList<Transfer.Domain.Cases.Transfer>>
{
    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        ShowInterestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await market.GetAsync(request.Subject, cancellationToken);

        if (status is null)
        {
            return new CaseResult.NichtAnsprechbar();
        }

        // ConsentGateSilent deliberately flies through: the endpoint turns it
        // into 503.
        if (!await tor.MaySeeMarketAsync(request.Subject, request.Company, cancellationToken))
        {
            return new CaseResult.NichtAnsprechbar();
        }

        if (!status.Ansprechbar)
        {
            // The release permits SEEING, not DISTURBING.
            return new CaseResult.NichtAnsprechbar();
        }

        // A second running case would be following up past the refusal.
        if (await store.GetCurrentAsync(request.Subject, request.Company, cancellationToken)
            is not null)
        {
            return new CaseResult.AlreadyRunning();
        }

        Transfer.Domain.Cases.Transfer caseItem;

        try
        {
            caseItem = Transfer.Domain.Cases.Transfer.Zeige_Interesse(
                request.Subject,
                request.Company,
                // Copied from the market status, so a later change does not
                // shift the terms of a running case retroactively.
                status.IsEmployed,
                request.Nachricht,
                clock.GetUtcNow());
        }
        catch (CaseError error)
        {
            return new CaseResult.Eingabe(error.Message);
        }

        await store.SaveAsync(caseItem, cancellationToken);
        await Melde(caseItem, cancellationToken);

        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        PersonMoveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await store.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Subject != request.Subject)
        {
            return new CaseResult.Unbekannt();
        }

        var now = clock.GetUtcNow();

        try
        {
            switch (request.Zug)
            {
                case Personenzug.AcceptConversationCommand:
                    caseItem.AcceptConversation(request.Subject, now);
                    break;
                case Personenzug.AcceptOfferCommand:
                    caseItem.AcceptOffer(request.Subject, now);
                    break;
                case Personenzug.ConfirmReleaseCommand:
                    caseItem.ConfirmRelease(request.Subject, now);
                    break;
                case Personenzug.Absagen:
                    caseItem.Sage_ab(request.Subject, now);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }
        catch (CaseError error)
        {
            return Zu(error);
        }

        await store.SaveAsync(caseItem, cancellationToken);

        // The person's moves are NOT notified: they know what they did, and a
        // mail to a company distribution list carrying a human being's name
        // would be the same leak channel, only the other way round.
        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        CompanySwitchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await store.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Company != request.Company)
        {
            return new CaseResult.Unbekannt();
        }

        var now = clock.GetUtcNow();

        try
        {
            switch (request.Zug)
            {
                case CompanySwitch.Complete:
                    caseItem.Schliesse_ab(now);
                    break;
                case CompanySwitch.WithdrawCommand:
                    caseItem.Withdraw(now);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }
        catch (CaseError error)
        {
            return Zu(error);
        }

        await store.SaveAsync(caseItem, cancellationToken);
        await Melde(caseItem, cancellationToken);

        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        MakeOfferCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await store.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Company != request.Company)
        {
            return new CaseResult.Unbekannt();
        }

        try
        {
            caseItem.MakeOffer(
                request.Text, request.Beginn, request.FeeCents, clock.GetUtcNow());
        }
        catch (CaseError error)
        {
            return Zu(error);
        }

        await store.SaveAsync(caseItem, cancellationToken);
        await Melde(caseItem, cancellationToken);

        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transfer.Domain.Cases.Transfer>> Handle(
        MyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForPersonAsync(request.Subject, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transfer.Domain.Cases.Transfer>> Handle(
        CompanyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForCompanyAsync(request.Company, cancellationToken);
    }

    /// <summary>Only the company's moves are notified.</summary>
    /// <remarks>
    /// In THE SAME transaction as the move (ADR-0025). There used to be an HTTP
    /// call after the commit here whose error was swallowed — the case stayed,
    /// the notification was gone for good.
    /// </remarks>
    private Task Melde(
        Transfer.Domain.Cases.Transfer caseItem, CancellationToken cancellationToken) =>
        outbox.NoteAsync(caseItem.Subject, NotificationKinds.Moved, cancellationToken);

    private static CaseResult Zu(CaseError error) => error switch
    {
        // Somebody else's case is like none from outside.
        NotYours => new CaseResult.Unbekannt(),
        TransitionNotAllowed => new CaseResult.StateConflict(error.Message),
        _ => new CaseResult.Eingabe(error.Message)
    };
}
