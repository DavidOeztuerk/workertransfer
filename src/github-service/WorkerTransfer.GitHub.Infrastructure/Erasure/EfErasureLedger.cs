using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Infrastructure.Persistence;

namespace WorkerTransfer.GitHub.Infrastructure.Erasure;

/// <summary>What an erasure touches in this service.</summary>
/// <remarks>
/// One statement, without exception. The login is public; the personal datum is
/// the <strong>link</strong> — "this platform person is that GitHub name". It
/// comes into being here, and here it falls.
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
