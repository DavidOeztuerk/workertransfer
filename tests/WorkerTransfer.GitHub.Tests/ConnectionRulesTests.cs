using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Rules that hang on the aggregate and not on a route.</summary>
/// <remarks>
/// This suite exists because a counter-probe showed a gap: the check "only a
/// verified connection may store a snapshot" could not be refuted over HTTP.
/// Today the handler reaches <c>StoreSnapshot</c> only after a successful
/// proof — so it is a second line of defence, and that is exactly what falls
/// silently when somebody later builds a "load now" button. Without it somebody
/// could enter a foreign user name and show that person's work as their own,
/// without ever having had access to the account.
/// </remarks>
public class ConnectionRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(20_000);

    private static Connection Opened() =>
        Connection.Open(new SubjectId(Guid.CreateVersion7()), "anna-dev");

    private static Repository Evidence(string name) =>
        new(name, "A repository", "Go", 0, $"https://github.com/anna/{name}", Now,
            ["Go"], ["cli"]);

    /// <summary>No proof, no snapshot.</summary>
    [Fact]
    public void An_unproven_connection_takes_no_snapshot()
    {
        var connection = Opened();

        var attempt = () => connection.StoreSnapshot(
            new Snapshot([Evidence("someone-elses-work")], true), Now);

        attempt.Should().Throw<NotVerified>();
        connection.Repositories.Should().BeEmpty();
        connection.FetchedAt.Should().BeNull();
    }

    /// <summary>A second proof is none.</summary>
    [Fact]
    public void Proving_twice_does_not_work()
    {
        var connection = Opened();
        connection.MarkVerified(Now);

        var attempt = () => connection.MarkVerified(Now.AddDays(1));

        attempt.Should().Throw<AlreadyVerified>();
        connection.VerifiedAt.Should().Be(Now);
    }

    /// <summary>
    /// The login has no <em>public</em> setter — it changes only over
    /// <see cref="Connection.RenameTo"/>, and that resets the proof.
    /// </summary>
    /// <remarks>
    /// A public setter would be the way to turn the name onto a foreign account
    /// after the proof.
    /// <para>
    /// What is checked is the <em>public</em> set method, not <c>CanWrite</c>:
    /// that reports a private setter as writable too, and a private one is
    /// right here — <c>RenameTo</c> needs it. Measured first, written second;
    /// the first attempt checked <c>CanWrite</c> and was red for that reason.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_login_has_no_public_setter()
    {
        typeof(Connection).GetProperty(nameof(Connection.Login))!
            .GetSetMethod(nonPublic: false).Should().BeNull();
    }

    /// <summary>
    /// The one-time string differs per connection.
    /// </summary>
    /// <remarks>
    /// Were it not, a single public gist would prove every account.
    /// </remarks>
    [Fact]
    public void Every_connection_gets_a_string_of_its_own()
    {
        var nonces = Enumerable.Range(0, 50)
            .Select(_ => Opened().Nonce)
            .ToList();

        nonces.Distinct().Should().HaveCount(50);
        nonces.Should().OnlyContain(
            value => value.Length >= 20 && !value.Contains('/') && !value.Contains('+'));
    }

    /// <summary>The description carries the string and a fixed prefix.</summary>
    [Fact]
    public void The_gist_description_carries_the_string()
    {
        Connection.GistDescription("abc123").Should().Be("workertransfer-verify-abc123");
    }
}
