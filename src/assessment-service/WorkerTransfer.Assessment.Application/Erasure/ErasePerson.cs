using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Application.Messages;
using WorkerTransfer.Assessment.Domain.Cases;

namespace WorkerTransfer.Assessment.Application.Erasure;

/// <summary>Erases everything this service holds about a person.</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>The cases, with their tasks, submissions and evaluations.</summary>
/// <remarks>
/// <para><strong>No retention case, and expressly not for the evaluation
/// either.</strong> What stands here is no record belonging to somebody else —
/// unlike a hired application or a paid transfer (ADR-0027 §3). An evaluation
/// that survived the erasure would be the reference letter ADR-0042 rules out,
/// only at a particularly bad moment. The answer is therefore always zero.</para>
///
/// <para><strong>The releases do not fall here.</strong> They stand in the
/// ledger, and consent-service is a recipient of the same cascade in its own
/// right. Revoking them here as well would be a second place for the same
/// erasure.</para>
/// </remarks>
public sealed class ErasePersonHandler(ICaseStore store)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    /// <returns>How much was deliberately kept. Always zero.</returns>
    public Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.DeleteAsync(request.Subject, cancellationToken);
    }
}
