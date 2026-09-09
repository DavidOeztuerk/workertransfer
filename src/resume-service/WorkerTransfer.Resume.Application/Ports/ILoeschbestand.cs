using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>What "delete" means in this service (ADR-0027 §2).</summary>
/// <remarks>
/// The requests run in two directions and the difference carries the design:
/// <list type="bullet">
/// <item>the person is the <em>subject</em> — the row falls. It <em>is</em> the
/// statement "company X asked about this human being".</item>
/// <item>the person is the <em>asker</em> — the row stays, without their name. A
/// recruiter deletes their private account; the proceeding belongs to the
/// company and is about a <em>third</em> person whose erasure nobody
/// asked for.</item>
/// </list>
/// <para>
/// No retention switch. No row class of this service was ever claimed to be
/// under a retention obligation, and a switch "just in case" is exactly the
/// precautionary assumption ADR-0027 §3 abolishes.
/// </para>
/// </remarks>
public interface ILoeschbestand
{
    /// <summary>Deletes everything about this person.</summary>
    /// <returns>What was left standing. Zero, always.</returns>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
