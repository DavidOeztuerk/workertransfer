using Noelia.Core.Identity;

namespace WorkerTransfer.Assessment.Domain.Audit;

/// <summary>One entry of the trail: what happened, to whom, when.</summary>
/// <remarks>No actor: no human being acts here, but the order one of them gave
/// earlier.</remarks>
/// <param name="Action">What happened.</param>
/// <param name="Target">Whom it concerned.</param>
/// <param name="OccurredAt">When.</param>
public sealed record AuditEntry(AuditAction Action, SubjectId Target, DateTimeOffset OccurredAt);
