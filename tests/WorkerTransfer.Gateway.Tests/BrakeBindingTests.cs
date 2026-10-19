using FluentAssertions;
using Noelia.Infrastructure.Models;
using Microsoft.Extensions.Configuration;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>Does <c>ocelot.json</c> really bind to Noelia's rate-limit options?</summary>
/// <remarks>
/// <para><strong>The reason for this suite is a measurement that
/// contradicted.</strong> Against the running stack six calls to
/// <c>/auth/register</c> all went through although 5 per minute stands there,
/// and the answer carried <c>X-RateLimit-Limit: 200</c> — a number that appears
/// nowhere in <c>ocelot.json</c>.</para>
///
/// <para><c>BrakeTests</c> could not find that: it checks that every
/// braked path has a route — a statement about the <em>selection</em>, not
/// about the <em>binding</em>. A configuration that never arrives still has
/// nothing but valid paths.</para>
///
/// <para>This suite closes the gap in the place where it lies: it reads the
/// same file the service reads, binds it to the same type and asks what comes
/// out.</para>
/// </remarks>
public sealed class BrakeBindingTests
{
    private static DistributedRateLimitingOptions Bound()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "gateway", "WorkerTransfer.Gateway", "ocelot.json");

        File.Exists(path).Should().BeTrue(
            "this suite reads the REAL route map — a copy would not answer "
            + "the question but double it");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.GetFullPath(path), optional: false)
            .Build();

        var options = new DistributedRateLimitingOptions();
        configuration
            .GetSection(DistributedRateLimitingOptions.SectionName)
            .Bind(options);

        return options;
    }

    /// <summary>
    /// <strong>The three defaults stand at 0, and that has to arrive.</strong>
    /// </summary>
    /// <remarks>
    /// A limit of 0 writes no counter — only for that reason do exclusively the
    /// five named paths count. If the section does not bind, Noelia's defaults
    /// apply (100 per minute), and then <em>every</em> asset fetch of the
    /// interface counts along, which travels through the same gateway.
    /// </remarks>
    [Fact]
    public void The_three_defaults_arrive_as_zero()
    {
        var options = Bound();

        options.RequestsPerMinute.Should().Be(0);
        options.RequestsPerHour.Should().Be(0);
        options.RequestsPerDay.Should().Be(0);
    }

    /// <summary>Per origin, never per user.</summary>
    /// <remarks>
    /// Noelia's default is <c>UserThenOrigin</c>. If that arrived, a signed-in
    /// person would count against themselves instead of against their origin —
    /// and the brake would lose exactly the property it stands at the entrance
    /// for.
    /// </remarks>
    [Fact]
    public void Counting_happens_per_origin()
    {
        Bound().Subject.Should().Be(RateLimitSubject.Origin);
    }

    /// <summary>The five paths with their numbers, as they stand.</summary>
    [Fact]
    public void The_five_limits_arrive()
    {
        var limits = Bound().EndpointSpecificLimits;

        limits.Should().ContainKey("/auth/login");
        limits["/auth/login"].RequestsPerMinute.Should().Be(20);
        limits["/auth/register"].RequestsPerMinute.Should().Be(5);
        limits["/auth/resend-verification"].RequestsPerMinute.Should().Be(3);
        limits["/auth/verify-email"].RequestsPerMinute.Should().Be(20);
        limits["/auth/refresh"].RequestsPerMinute.Should().Be(60);
    }

    /// <summary>And the exemption lists stay empty.</summary>
    /// <remarks>
    /// The binder <em>adds to</em> a collection and never replaces it. If
    /// loopback stood in Noelia's default, an empty list in the file would not
    /// get you anywhere against it — then nothing would brake locally, and you
    /// would not see it. Since 4.2.0 the defaults are empty; this test pins that
    /// it stays that way.
    /// </remarks>
    [Fact]
    public void The_exemption_lists_stay_empty()
    {
        var options = Bound();

        options.WhitelistedIps.Should().BeEmpty();
        options.WhitelistedEndpoints.Should().BeEmpty();
    }
}
