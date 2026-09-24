using FluentAssertions;
using Noelia.Infrastructure.Sovereignty;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// What Noelia's egress policy does with OUR addresses — measured before it is
/// registered anywhere.
/// </summary>
/// <remarks>
/// <para><strong>The reason for this suite is a near outage.</strong>
/// <c>AddSovereignPlatform</c> hangs <c>EgressGuardHandler</c> on <em>every</em>
/// client from the <c>IHttpClientFactory</c>, and the guard <em>refuses</em>
/// instead of logging. Our services call each other under container names —
/// <c>http://consent-service:8002</c>, not under an IP. The default allows
/// "loopback and RFC1918"; whether a <em>name</em> falls under that decides
/// about half the platform and stands in no summary.
/// </para>
/// <para>That is why the answer stands here as a measurement and not as an
/// assumption — and it stays, so that a later Noelia version does not change it
/// silently.</para>
/// </remarks>
public sealed class EgressTests
{
    /// <summary>The default as <c>AddSovereignPlatform</c> builds it.</summary>
    private static IEgressPolicy Default() =>
        Built(builder => builder.AllowLoopback().AllowPrivateNetworks());

    private static IEgressPolicy Built(Action<EgressPolicyBuilder> configure)
    {
        var builder = new EgressPolicyBuilder();
        configure(builder);

        return builder.Build();
    }

    /// <summary>
    /// <strong>A container name is neither loopback nor RFC1918.</strong>
    /// </summary>
    /// <remarks>
    /// If this suite goes red because the names are suddenly <em>allowed</em>,
    /// that is no good news: the policy would then be hitting names it cannot
    /// resolve, and allowing more than intended.
    /// </remarks>
    [Theory]
    [InlineData("http://consent-service:8002/consent/check")]
    [InlineData("http://identity-service:8001/auth/session")]
    [InlineData("http://notification-service:8010/notifications/send")]
    public void A_container_name_does_not_fall_under_the_default(string address)
    {
        Default().IsAllowed(new Uri(address)).Should().BeFalse(
            "otherwise the default would suffice for service-to-service traffic, "
            + "and the test below would be superfluous");
    }

    /// <summary>Allowed by name, and then it works.</summary>
    /// <remarks>
    /// That is the path WorkerTransfer has to take: every service names the
    /// hosts it really calls. The list must not be maintained by hand in doing
    /// so — it already stands in the environment (<c>Consent__BaseUrl</c>,
    /// <c>Jobs__BaseUrl</c>, …), and a second list beside it would be the one to
    /// go stale first.
    /// </remarks>
    [Fact]
    public void What_is_allowed_by_name_goes_through()
    {
        var policy = Built(builder => builder
            .AllowLoopback()
            .AllowPrivateNetworks()
            .Allow("consent-service", "api.anthropic.com"));

        policy.IsAllowed(new Uri("http://consent-service:8002/consent/check"))
            .Should().BeTrue();
        policy.IsAllowed(new Uri("https://api.anthropic.com/v1/messages"))
            .Should().BeTrue();

        // And what is not named stays out — that is the whole purpose.
        policy.IsAllowed(new Uri("https://telemetry.example.test/v1/traces"))
            .Should().BeFalse();
    }

    /// <summary>
    /// <strong>The two destinations that really go outside.</strong>
    /// </summary>
    /// <remarks>
    /// GitHub and the draft provider are the only hosts outside our own network
    /// that this system calls. Both have to be named, otherwise the proof of a
    /// GitHub connection and the cover letter die with "provider does not
    /// answer" — a message that looks like an outage at GitHub and is none.
    /// </remarks>
    [Theory]
    [InlineData("https://api.github.com/users/anna/repos")]
    [InlineData("https://api.anthropic.com/v1/messages")]
    public void The_two_outside_destinations_need_to_be_named(string address)
    {
        Default().IsAllowed(new Uri(address)).Should().BeFalse();
    }

    /// <summary>A private-range IP does fall under the default.</summary>
    /// <remarks>
    /// The counter-probe to the first suite: the default is not broken, it
    /// simply does not understand names. Without this half the finding above
    /// could also mean "the policy allows nothing at all".
    /// </remarks>
    [Theory]
    [InlineData("http://127.0.0.1:8002/consent/check")]
    [InlineData("http://10.0.0.5:8002/consent/check")]
    [InlineData("http://192.168.1.20:5432/")]
    public void A_private_address_falls_under_it(string address)
    {
        Default().IsAllowed(new Uri(address)).Should().BeTrue();
    }
}
