namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Wrong address, wrong password, or no such account.</summary>
/// <remarks>
/// One exception for all three. Which of them it was belongs in the audit
/// trail, never in the answer: a different reply per case answers "is this
/// person here?" to anyone who asks.
/// </remarks>
public sealed class InvalidCredentialsException()
    : Exception("Invalid credentials");

/// <summary>The account exists and its address is not confirmed yet.</summary>
/// <remarks>
/// Separate from <see cref="AccountDisabledException"/> because the answers
/// differ: this one is answered 403 so the interface can offer to send the mail
/// again. It is only ever raised once the password was correct, so it states
/// nothing the password did not already prove.
/// </remarks>
public sealed class EmailNotConfirmedException()
    : Exception("Confirm your email address to sign in");

/// <summary>The account is not active.</summary>
public sealed class AccountDisabledException()
    : Exception("Account is not active");
