using MediatR;
using WorkerTransfer.Messages.Application.Messages;
using WorkerTransfer.Messages.Application.Ports;

namespace WorkerTransfer.Messages.Application.Behaviors;

/// <summary>Runs every command in a transaction.</summary>
/// <remarks>
/// Here instead of in every handler, because the handler that forgets it is the
/// one nobody notices: everything it writes still lands — just not together.
/// <para>
/// Here the message and the outbox note telling the other side commit
/// together or not at all (ADR-0025): a message the other side never hears of
/// would be a reply nobody reads.
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
