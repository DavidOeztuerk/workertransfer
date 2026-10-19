namespace WorkerTransfer.Assessment.Domain.Audit;

/// <summary>What this service's trail records.</summary>
/// <remarks>
/// One value, because this service records one thing: that the erasure cascade
/// reached it. What it deleted stands nowhere — that would be a copy of what
/// just disappeared.
/// </remarks>
public enum AuditAction
{
    /// <summary>Everything this service held about a person fell with the erasure.</summary>
    Erased
}
