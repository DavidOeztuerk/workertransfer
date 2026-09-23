using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Ablage;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Infrastructure.Persistence;

namespace WorkerTransfer.Resume.Infrastructure.Erasure;

/// <summary>What an erasure really touches in this service.</summary>
/// <remarks>
/// One pairing carries the whole design: a row <em>about</em> the person falls,
/// a row where the person merely <em>acted</em> keeps standing without their
/// name. The same distinction holds
/// for the requests and for the trail, and it has to, or a recruiter deleting
/// their private account would erase proceedings that belong to their employer
/// and concern somebody else entirely.
/// <para>
/// No retention switch anywhere. Nothing here was ever claimed to be under a
/// retention obligation, and a switch "just in case" is the precautionary
/// assumption ADR-0027 §3 abolishes.
/// </para>
/// </remarks>
public sealed class EfErasureLedger(ResumeDbContext context, IDocumentStore documentStore)
    : IErasureLedger
{
    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        // The résumé. `resumes.id` IS the subject id, and positions and
        // education lie as jsonb in the same row, so they fall with it.
        await context.Resumes
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // THE DOCUMENTS — rows AND files.
        //
        // First read the keys, then delete the rows, then the files: after
        // `ExecuteDelete` nobody knows any more where the bytes lay, and they
        // would lie there for ever. The interface would look empty, the disk
        // would not — exactly the broken promise ADR-0027 rules out.
        //
        // The files last, because a break-off in between leaves a file without
        // a row: unreachable and cleanable. The other way round a row without a
        // file would remain and look like a document.
        var keys = await context.Documents
            .Where(row => row.SubjectId == subject.Value)
            .Select(row => row.StorageKey)
            .ToListAsync(cancellationToken);

        await context.Documents
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // THE INDEX FALLS WITH IT (ADR-0043). It holds what was read in the
        // documents on request — canonical names from a reference, and so
        // personal. An index over a human being's texts may only exist at all
        // as long as it lies in the same service as the files AND is hit by the
        // same erasure; without this line the condition under which it was
        // allowed to be built would be broken. That is measured in the suite
        // (`RecognitionJourneyTests`), not here.
        await context.Terms
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var key in keys)
        {
            await documentStore.DeleteAsync(key, cancellationToken);
        }

        // Asked ABOUT them: the row is the statement "company X asked about
        // this human being".
        await context.Requests
            .Where(row => row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Asked BY them: the proceeding belongs to the company and concerns a
        // third person. Only the name falls away.
        await context.Requests
            .Where(row => row.RequestedBy == subject.Value)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.RequestedBy, (Guid?)null),
                cancellationToken);

        await context.AuditEntries
            .Where(row => row.TargetId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        await context.AuditEntries
            .Where(row => row.ActorId == subject.Value)
            .ExecuteUpdateAsync(
                set => set.SetProperty(row => row.ActorId, (Guid?)null),
                cancellationToken);

        // An outstanding notification to an account that no longer exists.
        await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

        // Nothing stays. The receipt says so rather than leaving the origin to
        // guess (ADR-0027 §3).
        return 0;
    }
}
