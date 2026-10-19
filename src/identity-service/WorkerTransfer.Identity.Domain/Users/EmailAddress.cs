using System.Net.Mail;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Whether an address is deliverable.</summary>
/// <remarks>
/// Checked with the same parser that sends later. A regular expression of our
/// own would be a second grammar beside <see cref="MailAddress"/>'s, and what
/// passed here could fail there — and then the row stands in the database while
/// the mail never goes out.
/// <para>
/// What is checked is the FORM, never the existence: whether there is a mailbox
/// behind the address is something this service must not give away
/// (<c>/auth/register</c> answers identically for known and unknown addresses).
/// </para>
/// </remarks>
public static class EmailAddress
{
    /// <summary>The ceiling from RFC 5321: 254 characters.</summary>
    public const int MaxLength = 254;

    /// <summary>Can a mail be built from it?</summary>
    /// <param name="address">What somebody typed.</param>
    /// <returns><c>true</c> when sending will not fail over it.</returns>
    public static bool IsDeliverable(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > MaxLength)
        {
            return false;
        }

        if (!MailAddress.TryCreate(address.Trim(), out var built))
        {
            return false;
        }

        // A bare address, not "Anna <a@b.de>": otherwise that is what would
        // stand in the column that identifies the human being.
        if (!string.Equals(built.Address, address.Trim(), StringComparison.Ordinal)
            || built.DisplayName.Length > 0)
        {
            return false;
        }

        // `a@localhost` is syntactically permitted and not deliverable on the
        // open network.
        var dot = built.Host.LastIndexOf('.');

        return dot > 0 && dot < built.Host.Length - 1;
    }
}
