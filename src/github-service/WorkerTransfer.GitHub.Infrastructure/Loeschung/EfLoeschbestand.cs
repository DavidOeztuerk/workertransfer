using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Infrastructure.Persistence;

namespace WorkerTransfer.GitHub.Infrastructure.Erasure;

/// <summary>Was eine Löschung in diesem Dienst anfasst.</summary>
/// <remarks>
/// Eine Anweisung, ohne Ausnahme. Der Login ist öffentlich; das Personendatum
/// ist die <strong>Verknüpfung</strong> — „dieser Plattform-Mensch ist jener
/// GitHub-Name". Sie entsteht hier, und hier fällt sie.
/// </remarks>
public sealed class EfErasureLedger(GitHubDbContext context) : IErasureLedger
{
    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        await context.Connections
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        return 0;
    }
}
