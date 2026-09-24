using FluentAssertions;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>The route map does not hang on the order of its lines.</summary>
/// <remarks>
/// Measured, and the reason the <c>Priority</c> values are no decoration:
/// <strong>without them the file order decides.</strong> Setting every
/// exception to the same value looked harmless at first — the suite stayed
/// green because the lines happened to stand in the right order. Only reversing
/// them made eight routes fall over.
/// <para>
/// So this is the check that protects the next person who appends a route at
/// the bottom: the same questions to the same map, only loaded backwards.
/// </para>
/// </remarks>
[Collection(ReversedCollection.Name)]
public class RouteOrderTests(ReversedLandscape landscape)
{
    /// <summary>The exceptions — exactly the ones that fall over without a priority.</summary>
    [Theory]
    [InlineData("/jobs/7f000001-0000-0000-0000-000000000000/applications", "applications")]
    [InlineData("/companies/me/jobs", "jobs")]
    [InlineData("/companies/me/application-stats", "applications")]
    [InlineData("/companies/me/profile", "companies")]
    [InlineData("/companies/by-slug/example-ltd", "companies")]
    [InlineData("/companies/7f000001-0000-0000-0000-000000000000/profile", "companies")]
    [InlineData("/me/notification-preferences", "notification")]
    public async Task The_exceptions_win_backwards_too(string path, string expected)
    {
        var (service, _) = await landscape.Ask(path);

        service.Should().Be(expected);
    }

    /// <summary>And the ordinary cases stay where they are.</summary>
    [Theory]
    [InlineData("/me", "identity")]
    [InlineData("/companies", "identity")]
    [InlineData("/jobs", "jobs")]
    [InlineData("/notifications", "notification")]
    public async Task The_ordinary_cases_stay_backwards_too(string path, string expected)
    {
        var (service, _) = await landscape.Ask(path);

        service.Should().Be(expected);
    }

}
