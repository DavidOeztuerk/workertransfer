using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Infrastructure.Persistence;

namespace WorkerTransfer.GitHub.Infrastructure.Loeschung;

/// <summary>Was eine Löschung in diesem Dienst anfasst.</summary>
/// <remarks>
/// Eine Anweisung, ohne Ausnahme. Der Login ist öffentlich; das Personendatum
/// ist die <strong>Verknüpfung</strong> — „dieser Plattform-Mensch ist jener
/// GitHub-Name". Sie entsteht hier, und hier fällt sie.
/// </remarks>
public sealed class EfLoeschbestand(GitHubDbContext kontext) : ILoeschbestand
{
    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        await kontext.Verbindungen
            .Where(zeile => zeile.Id == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        return 0;
    }
}
