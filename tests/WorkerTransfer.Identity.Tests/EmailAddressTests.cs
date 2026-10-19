using FluentAssertions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>An address that is not deliverable must create no account.</summary>
public class EmailAddressTests
{
    [Theory]
    [InlineData("bus")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@example.org")]
    [InlineData("anna@")]
    [InlineData("anna example.org")]
    // Syntactically permitted, not deliverable on the open network.
    [InlineData("anna@localhost")]
    // A valid sender line, but no mailbox.
    [InlineData("Anna <anna@example.org>")]
    public void What_is_not_deliverable_falls_through(string address) =>
        EmailAddress.IsDeliverable(address).Should().BeFalse();

    [Theory]
    [InlineData("anna@example.org")]
    [InlineData("anna.example@company.co.uk")]
    [InlineData("anna+application@example.org")]
    [InlineData("a@b.de")]
    [InlineData("jörg@example.org")]
    public void What_is_deliverable_gets_through(string address) =>
        EmailAddress.IsDeliverable(address).Should().BeTrue();

    [Fact]
    public void An_immoderately_long_address_falls_through()
    {
        var tooLong = new string('a', EmailAddress.MaxLength) + "@example.org";

        EmailAddress.IsDeliverable(tooLong).Should().BeFalse();
    }

    /// <summary>The check and the sending speak the same grammar.</summary>
    /// <remarks>
    /// What gets through here has to be buildable as a <c>MailMessage</c> —
    /// otherwise the row stands in the database and the mail never goes out.
    /// </remarks>
    [Theory]
    [InlineData("anna@example.org")]
    [InlineData("anna.example@company.co.uk")]
    [InlineData("a@b.de")]
    public void What_gets_through_can_also_be_sent(string address)
    {
        EmailAddress.IsDeliverable(address).Should().BeTrue();

        var build = () => new System.Net.Mail.MailMessage("sender@example.org", address);

        build.Should().NotThrow();
    }
}
