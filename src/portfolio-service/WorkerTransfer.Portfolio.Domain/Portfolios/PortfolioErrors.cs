namespace WorkerTransfer.Portfolio.Domain.Portfolios;

/// <summary>Something about the input is wrong.</summary>
/// <remarks>
/// One root for every input error, so the Api layer maps them to 422 in one
/// place instead of five.
/// </remarks>
public abstract class InputError(string message) : Exception(message);

/// <summary>A text field is empty or too long.</summary>
public sealed class TextError(string field, string reason)
    : InputError($"{field} {reason}.");

/// <summary>The link is no good.</summary>
/// <remarks>
/// A portfolio link gets clicked by strangers. In a field that later ends up
/// in a browser, <c>javascript:</c> and <c>data:</c> are not an exotic edge
/// case but the ordinary shape of an attack.
/// </remarks>
public sealed class LinkError(string reason) : InputError(reason);

/// <summary>The year lies outside the possible.</summary>
public sealed class YearError(string reason) : InputError(reason);

/// <summary>The attachment name is not one.</summary>
public sealed class AttachmentError() : InputError("Das ist kein gültiger Anhangname.");

/// <summary>Too many entries.</summary>
public sealed class TooManyEntries(int atMost)
    : InputError($"Höchstens {atMost} Einträge sind erlaubt.");
