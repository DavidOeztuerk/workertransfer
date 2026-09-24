using MediatR;
using WorkerTransfer.Scout.Application.Messages;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Application.Behaviors;

/// <summary>Runs every command in exactly one transaction.</summary>
/// <remarks>
/// Here instead of in every handler, because the handler that forgets it is the
/// one nobody notices: what it writes lands anyway — only not together. Queries
/// pass through, see <see cref="IQueryMessage{TResponse}"/>.
/// <para>
/// In this service more hangs on it than one line: the note "your profile was
/// discovered" arises in the outbox and has to be committed together with the
/// command or not at all (ADR-0025).
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
