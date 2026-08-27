using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Infrastructure.Persistence;

namespace WorkerTransfer.Resume.Infrastructure.Loeschung;

/// <summary>What an erasure really touches in this service.</summary>
/// <remarks>
/// Five statements, and the pairing of the middle two is the whole design:
/// a row <em>about</em> the person falls, a row where the person merely
/// <em>acted</em> keeps standing without their name. The same distinction holds
/// for the requests and for the trail, and it has to, or a recruiter deleting
/// their private account would erase proceedings that belong to their employer
/// and concern somebody else entirely.
/// <para>
/// No retention switch anywhere. Nothing here was ever claimed to be under a
/// retention obligation, and a switch "just in case" is the precautionary
/// assumption ADR-0027 §3 abolishes.
/// </para>
/// </remarks>
public sealed class EfLoeschbestand(ResumeDbContext kontext) : ILoeschbestand
{
    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default)
    {
        // The résumé. `resumes.id` IS the subject id, and positions and
        // education lie as jsonb in the same row, so they fall with it.
        await kontext.Lebenslaeufe
            .Where(zeile => zeile.Id == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Asked ABOUT them: the row is the statement "company X asked about
        // this human being".
        await kontext.Anfragen
            .Where(zeile => zeile.SubjectId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Asked BY them: the proceeding belongs to the company and concerns a
        // third person. Only the name falls away.
        await kontext.Anfragen
            .Where(zeile => zeile.RequestedBy == wer.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(zeile => zeile.RequestedBy, (Guid?)null),
                cancellationToken);

        await kontext.Pruefspur
            .Where(zeile => zeile.TargetId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await kontext.Pruefspur
            .Where(zeile => zeile.ActorId == wer.Value)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(zeile => zeile.ActorId, (Guid?)null),
                cancellationToken);

        // An outstanding notification to an account that no longer exists.
        await kontext.Set<OutboxZeile>()
            .Where(zeile => zeile.UserId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Nothing stays. The receipt says so rather than leaving the origin to
        // guess (ADR-0027 §3).
        return 0;
    }
}
