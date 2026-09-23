using MediatR;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Application.Behaviors;

/// <summary>Runs every command inside one transaction.</summary>
/// <remarks>
/// Here rather than in each handler, because the handler that forgets it is the
/// one nobody notices: everything it writes still lands, just not together —
/// and the outbox row that lands without its request is a notification about
/// something that never happened. Queries pass straight through.
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
