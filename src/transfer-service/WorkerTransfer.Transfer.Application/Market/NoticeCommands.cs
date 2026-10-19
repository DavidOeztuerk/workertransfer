using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Domain.Market;

namespace WorkerTransfer.Transfer.Application.Market;

/// <summary>State one's own notice period (VISION-FRAGEN H2).</summary>
public sealed record StateNoticeCommand(SubjectId Subject, int Length, NoticeUnit Unit, NoticeEnd End)
    : ICommandMessage<NoticePeriod>;

/// <summary>Withdraw it — "I no longer say".</summary>
public sealed record WithdrawNoticeCommand(SubjectId Subject) : ICommandMessage<bool>;

/// <summary>One's own notice period, or <c>null</c>: not stated.</summary>
/// <remarks>There is no query for somebody else's — a company has none to ask.</remarks>
public sealed record MyNoticeQuery(SubjectId Subject) : IQueryMessage<NoticePeriod?>;

/// <summary>Writes and reads the notice period — only the person's own.</summary>
public sealed class NoticeCommands(INoticeStore store, TimeProvider clock) :
    IRequestHandler<StateNoticeCommand, NoticePeriod>,
    IRequestHandler<WithdrawNoticeCommand, bool>,
    IRequestHandler<MyNoticeQuery, NoticePeriod?>
{
    /// <inheritdoc />
    public async Task<NoticePeriod> Handle(StateNoticeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notice = NoticePeriod.State(request.Subject, request.Length, request.Unit, request.End, clock.GetUtcNow());
        await store.SaveAsync(notice, cancellationToken);
        return notice;
    }

    /// <inheritdoc />
    public async Task<bool> Handle(WithdrawNoticeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await store.DeleteAsync(request.Subject, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public Task<NoticePeriod?> Handle(MyNoticeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Subject, cancellationToken);
    }
}
