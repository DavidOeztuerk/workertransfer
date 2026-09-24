using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>How a command on an application turned out.</summary>
/// <remarks>
/// A result instead of thrown exceptions, so the mapping to status codes stands
/// in <em>one</em> place: seven routes that each decide for themselves what a
/// foreign case is drift apart — and the first one that answers 403 instead of
/// 404 gives away that the application exists.
/// </remarks>
public abstract record ApplicationResult
{
    private ApplicationResult()
    {
    }

    /// <summary>It worked.</summary>
    public sealed record Done(JobApplication Application) : ApplicationResult;

    /// <summary>The job does not exist publicly.</summary>
    public sealed record NoJob : ApplicationResult;

    /// <summary>
    /// Does not exist OR belongs to someone else — the same from outside.
    /// </summary>
    /// <remarks>
    /// Telling the two apart would mean confirming on demand that a foreign
    /// application exists.
    /// </remarks>
    public sealed record Unknown : ApplicationResult;

    /// <summary>The input is fine, the state does not fit.</summary>
    public sealed record StateConflict(string Reason) : ApplicationResult;

    /// <summary>Something is wrong with the input.</summary>
    public sealed record InvalidInput(string Reason) : ApplicationResult;
}
