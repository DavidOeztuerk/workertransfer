using MediatR;
using WorkerTransfer.Jobs.Application.Messages;
using WorkerTransfer.Jobs.Application.Ports;

namespace WorkerTransfer.Jobs.Application.Behaviors;

/// <summary>Runs every command in exactly one transaction.</summary>
/// <remarks>
/// Here instead of in every handler, because the handler that forgets it is the
/// one nobody notices: what it writes lands anyway — only not together. Queries
/// pass through, see <see cref="IQueryMessage{TResponse}"/>.
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
