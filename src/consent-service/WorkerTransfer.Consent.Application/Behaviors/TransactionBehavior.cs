using MediatR;
using WorkerTransfer.Consent.Application.Messages;
using WorkerTransfer.Consent.Application.Ports;

namespace WorkerTransfer.Consent.Application.Behaviors;

/// <summary>Runs every command inside one transaction.</summary>
/// <remarks>
/// Here rather than in each handler, because the handler that forgets it is the
/// one nobody notices: everything it writes still lands, just not together.
/// Queries pass straight through — see <see cref="IQueryMessage{TResponse}"/>.
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
