using MediatR;
using WorkerTransfer.Assessment.Application.Messages;
using WorkerTransfer.Assessment.Application.Ports;

namespace WorkerTransfer.Assessment.Application.Behaviors;

/// <summary>Runs every command in a transaction.</summary>
/// <remarks>
/// Here instead of in every handler, because the handler that forgets it is the
/// one nobody notices: everything it writes still lands — just not together.
/// <para>
/// In this service more than one row hangs on it: the evaluation and the outbox
/// entry that tells the person about it are committed together or not at all
/// (ADR-0025). An evaluation without that entry would be a judgement the person
/// never hears of — and exactly that is not built here.
/// </para>
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc />
    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        return request is ICommandMessage
            ? unitOfWork.InTransactionAsync(next.Invoke, cancellationToken)
            : next(cancellationToken);
    }
}
