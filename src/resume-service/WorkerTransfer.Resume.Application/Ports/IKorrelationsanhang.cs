using Noelia.Core.Identity;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>Builds a trail entry with the running request's id already in it.</summary>
/// <remarks>
/// A handler should not have to remember to attach the correlation id, because
/// the one that forgets writes an entry that cannot be joined to anything —
/// and nothing shows it until somebody is trying to reconstruct what happened.
/// </remarks>
public interface IKorrelationsanhang
{
    /// <summary>One entry, correlated.</summary>
    AuditEntry Eintrag(
        AuditAction action,
        DateTimeOffset geschehen,
        SubjectId? akteur = null,
        TenantId? company = null,
        SubjectId? betroffene = null);
}
