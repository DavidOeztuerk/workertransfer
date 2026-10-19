namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>The headline is missing or too long.</summary>
/// <param name="reason">What is wrong with it.</param>
public sealed class HeadlineError(string reason) : InputError($"The headline {reason}.");

/// <summary>The free text is too long.</summary>
public sealed class TextError()
    : InputError($"The text may have at most {PersonProfile.MaxTextLength} characters.");

/// <summary>The location is too long.</summary>
public sealed class LocationError()
    : InputError($"The location may have at most {PersonProfile.MaxLocationLength} characters.");

/// <summary>The wish to the writing aid is too long.</summary>
/// <remarks>
/// It is an instruction ("shorter", "more matter-of-fact"), not content — the
/// content stands in the profile and is long since limited there. Unbraked,
/// this field would be the one way by which a caller sends any amount of text
/// to the foreign provider: it comes from the body of the request and passes no
/// value object on its way.
/// </remarks>
public sealed class WishError()
    : InputError($"The wish may have at most {PersonProfile.MaxWishLength} characters.");

/// <summary>The XING link is not the address of a XING profile.</summary>
public sealed class XingError()
    // web/src/features/person/api/profile.ts matches this sentence.
    : InputError("The XING link must be a XING profile address (https://www.xing.com/profile/…).");
