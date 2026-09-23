using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Messages;
using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Application.Withdrawal;

/// <summary>Withdraws the adverts of a company.</summary>
/// <remarks>
/// The special case from ADR-0027 §7: the last person with <c>role='admin'</c>
/// has erased their account. An intent about a <b>company</b>, not about a human
/// being — and it expressly does <em>not</em> count towards the completeness
/// evidence of the erasure, otherwise a silent jobs-service would keep the
/// erasure of a human being open.
/// </remarks>
public sealed record WithdrawAdvertsCommand(TenantId Company) : ICommandMessage<int>;

/// <inheritdoc cref="WithdrawAdvertsCommand" />
public sealed class WithdrawAdvertsHandler(IJobStore store, TimeProvider clock)
    : IRequestHandler<WithdrawAdvertsCommand, int>
{
    /// <inheritdoc />
    public Task<int> Handle(
        WithdrawAdvertsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Withdrawn, not erased: a company is no natural person, and its adverts
        // still belong to it then. But an unsupervised job advert is worse than
        // none — applications would go to nobody.
        return store.WithdrawAsync(request.Company, clock.GetUtcNow(), cancellationToken);
    }
}
