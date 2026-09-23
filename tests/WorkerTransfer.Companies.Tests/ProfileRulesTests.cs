using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Tests;

/// <summary>Rules that hang on the aggregate and not on a route.</summary>
/// <remarks>
/// This suite exists because a counter-probe showed a gap: the order "check
/// everything first, then write" could not be refuted over HTTP. The store
/// returns read rows as a <em>new</em> aggregate and writes only through
/// <c>SaveAsync</c>, which after an exception is not called at all — the
/// half-changed instance is simply discarded. So the rule protects whoever
/// keeps the aggregate in hand beyond the failure, and that is exactly where
/// it has to be checked.
/// </remarks>
public class ProfileRulesTests
{
    private static EmployerProfile Profile() =>
        EmployerProfile.Create(
            new TenantId(Guid.CreateVersion7()),
            "example",
            "First version",
            "About us.",
            "https://example.test",
            ["Berlin"],
            ["Remote work"],
            DateTimeOffset.UnixEpoch);

    /// <summary>
    /// A refused form must not leave a half-changed aggregate behind.
    /// </summary>
    [Fact]
    public void A_refused_change_leaves_the_aggregate_untouched()
    {
        var profile = Profile();

        var attempt = () => profile.Change(
            "Second version", "Entirely different.", "ftp://example.test",
            ["Hamburg"], ["Company bike"], DateTimeOffset.UnixEpoch.AddDays(1));

        attempt.Should().Throw<LinkError>();

        profile.DisplayName.Should().Be("First version");
        profile.About.Should().Be("About us.");
        profile.Website.Should().Be("https://example.test");
        profile.Locations.Should().Equal("Berlin");
        profile.Benefits.Should().Equal("Remote work");
        profile.ChangedAt.Should().Be(DateTimeOffset.UnixEpoch);
    }

    /// <summary>The slug has no way of changing.</summary>
    /// <remarks>
    /// Not "is not changed" but <em>cannot</em> be: there is no assignment for
    /// it. This check falls the moment somebody adds one — and that is the
    /// point at which a shared link begins to break.
    /// </remarks>
    [Fact]
    public void The_slug_has_no_setter()
    {
        typeof(EmployerProfile)
            .GetProperty(nameof(EmployerProfile.Slug))!
            .CanWrite.Should().BeFalse();
    }

    /// <summary>Umlauts are decomposed, their base letters stay.</summary>
    [Theory]
    [InlineData("Grün & Söhne GmbH", "grun-sohne-gmbh")]
    [InlineData("  Führende   Technik  ", "fuhrende-technik")]
    [InlineData("ACME", "acme")]
    [InlineData("株式会社", Slug.Fallback)]
    [InlineData("", Slug.Fallback)]
    [InlineData("---", Slug.Fallback)]
    public void A_name_becomes_a_slug(string name, string expected)
    {
        Slug.From(name).Should().Be(expected);
    }

    /// <summary>A slug never ends on a hyphen.</summary>
    /// <remarks>
    /// The counter appends <c>-2</c>; a slug that already ends on a hyphen
    /// would yield <c>brand--2</c>.
    /// </remarks>
    [Fact]
    public void A_truncated_slug_does_not_end_on_a_hyphen()
    {
        var veryLong = string.Join(" ", Enumerable.Repeat("word", 40));

        var slug = Slug.From(veryLong);

        slug.Length.Should().BeLessThanOrEqualTo(Slug.MaxLength);
        slug.Should().NotEndWith("-");
        slug.Should().NotStartWith("-");
    }
}
