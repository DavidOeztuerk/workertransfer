namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>Something about the input is wrong.</summary>
public abstract class InputError(string message) : Exception(message);

/// <summary>A text field is empty or too long.</summary>
public sealed class TextError(string field, string reason) : InputError($"{field} {reason}.");

/// <summary>Too many requirements, or one too long.</summary>
public sealed class SkillError(string reason) : InputError(reason);

/// <summary>This step is not possible from here.</summary>
public sealed class TransitionNotAllowed(JobState now, JobState wanted)
    : InputError($"A {now} job cannot become {wanted}.");

/// <summary>The wish to the drafting aid is too long.</summary>
/// <remarks>
/// Unchecked, this field would be the one way through which a caller sends any
/// amount of text to the foreign provider: it comes from the request body and
/// passes no value object.
/// </remarks>
public sealed class WishError()
    : InputError($"The wish may have at most {Job.MaxWishLength} characters.");
