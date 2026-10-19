namespace WorkerTransfer.Companies.Domain.EmployerProfiles;

/// <summary>Something about the input is wrong.</summary>
public abstract class ProfileError(string message) : Exception(message);

/// <summary>A text field is empty or too long.</summary>
public sealed class TextError(string field, string what) : ProfileError($"{field} {what}");

/// <summary>The link is no good.</summary>
/// <remarks>
/// The same rule as for portfolio links (ADR-0021): a link gets clicked by
/// strangers, and <c>javascript:</c> in a field that ends up in a browser is
/// not an edge case.
/// </remarks>
public sealed class LinkError(string what) : ProfileError(what);

/// <summary>There are too many entries.</summary>
public sealed class TooManyEntries(string field, int limit)
    : ProfileError($"At most {limit} {field} are allowed");
