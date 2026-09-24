using MediatR;
using WorkerTransfer.Advisor.Application.Messages;
using WorkerTransfer.Advisor.Application.Ports;

namespace WorkerTransfer.Advisor.Application.Behaviors;

/// <summary>Runs every command in a transaction.</summary>
/// <remarks>
/// Here instead of in every handler, because the handler that forgets it is the
/// one nobody notices: everything it writes still lands — just not together.
/// <para>
/// In this service more than one row hangs on it: an opened conversation and
/// the outbox entry that tells the person about it are committed together or
/// not at all (ADR-0025).
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
