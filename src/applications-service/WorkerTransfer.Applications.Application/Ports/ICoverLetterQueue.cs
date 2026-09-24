using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Writing and revising carry on behind the request.</summary>
public enum CoverLetterJobKind
{
    /// <summary>A new cover letter.</summary>
    Write,

    /// <summary>Carry out open comments.</summary>
    Revise
}

/// <summary>A job for the model, bound to the caller.</summary>
/// <param name="Subject">Whose draft.</param>
/// <param name="DraftId">Which draft.</param>
/// <param name="Bearer">The access token, so the lookup sees the same as the caller.</param>
/// <param name="Kind">Write or revise.</param>
public sealed record CoverLetterJob(
    SubjectId Subject, Guid DraftId, string? Bearer, CoverLetterJobKind Kind);

/// <summary>Takes jobs without waiting for the model.</summary>
public interface ICoverLetterQueue
{
    /// <summary>Queues the job. <c>false</c> when it is already running.</summary>
    bool Enqueue(CoverLetterJob job);

    /// <summary>Whether this draft is being written right now.</summary>
    bool IsRunning(Guid draftId);
}
