namespace WorkerTransfer.Profile.Domain;

/// <summary>What a person entered does not fit into a profile.</summary>
/// <remarks>
/// One common root, so the endpoints have <em>one</em> place where "not like
/// that" becomes a 422. Without it every endpoint catches three or four types
/// one by one, and the next error to arrive is forgotten at one of those places
/// — and comes out there as a 500.
/// <para>
/// The messages never carry what somebody wrote: they name the rule ("at most
/// 120 characters"), not the text. An error message ends up in logs and in
/// screenshots.
/// </para>
/// </remarks>
/// <param name="message">Which rule is broken.</param>
public abstract class InputError(string message) : Exception(message);
