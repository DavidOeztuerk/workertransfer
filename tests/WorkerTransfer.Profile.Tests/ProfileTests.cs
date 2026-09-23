using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Skills;

namespace WorkerTransfer.Profile.Tests;

/// <summary>The aggregate: narrow, without visibility, all or nothing.</summary>
public class ProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static PersonProfile Sample(
        string headline = "Developer",
        string text = "I build services.",
        string location = "Hamburg",
        bool remote = true,
        params string[] skills) =>
        PersonProfile.Create(
            SubjectId.New(), headline, text, location, remote,
            SkillList.From(skills), Now);

    /// <summary>
    /// ADR-0020: whether a profile may be shown stands exclusively in the
    /// consent ledger. A field here would be a second truth — and the one
    /// somebody forgets to change along.
    /// </summary>
    /// <remarks>
    /// The German needles stay: the tree spoke German for a long time, and a
    /// field named in the old language would be the same defect.
    /// </remarks>
    [Theory]
    [InlineData("sichtbar")]
    [InlineData("visib")]
    [InlineData("public")]
    [InlineData("oeffentlich")]
    [InlineData("freigabe")]
    [InlineData("release")]
    public void The_profile_has_no_visibility_field(string forbidden)
    {
        var names = typeof(PersonProfile).GetProperties().Select(property => property.Name);

        names.Should().NotContain(
            name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_person_is_the_key()
    {
        var subject = SubjectId.New();

        PersonProfile.Create(subject, "Title", "", "", false, SkillList.Empty, Now)
            .Subject.Should().Be(subject);
    }

    [Fact]
    public void Whitespace_falls_away()
    {
        var profile = Sample(headline: "  Developer  ", text: "  Hello  ", location: "  Kiel ");

        profile.Headline.Should().Be("Developer");
        profile.Text.Should().Be("Hello");
        profile.Location.Should().Be("Kiel");
    }

    [Fact]
    public void Without_a_headline_no_profile_arises()
    {
        var action = () => Sample(headline: "   ");

        action.Should().Throw<HeadlineError>();
    }

    [Fact]
    public void A_headline_that_is_too_long_is_refused()
    {
        var action = () =>
            Sample(headline: new string('x', PersonProfile.MaxHeadlineLength + 1));

        action.Should().Throw<HeadlineError>();
    }

    [Fact]
    public void A_text_that_is_too_long_is_refused()
    {
        var action = () => Sample(text: new string('x', PersonProfile.MaxTextLength + 1));

        action.Should().Throw<TextError>();
    }

    [Fact]
    public void A_location_that_is_too_long_is_refused()
    {
        var action = () => Sample(location: new string('x', PersonProfile.MaxLocationLength + 1));

        action.Should().Throw<LocationError>();
    }

    /// <summary>
    /// A refused form must not leave a half-changed aggregate behind: whoever
    /// changes the location and sends an empty headline with it keeps both as
    /// they were.
    /// </summary>
    [Fact]
    public void A_refused_change_changes_nothing_at_all()
    {
        var profile = Sample();

        var action = () => profile.Change(
            "", "New text", "Berlin", false, SkillList.Empty, Now.AddHours(1));

        action.Should().Throw<HeadlineError>();
        profile.Text.Should().Be("I build services.");
        profile.Location.Should().Be("Hamburg");
        profile.RemoteOk.Should().BeTrue();
        profile.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void A_change_sets_the_timestamp_and_leaves_the_creation_alone()
    {
        var profile = Sample();
        var later = Now.AddDays(3);

        profile.Change("New", "Text", "Kiel", false, SkillList.From(["Go"]), later);

        profile.UpdatedAt.Should().Be(later);
        profile.CreatedAt.Should().Be(Now);
        profile.Skills.Values.Should().Equal("Go");
    }

    /// <summary>
    /// Refusing a stored row on reading would mean taking somebody's profile
    /// away because an upper limit has changed.
    /// </summary>
    [Fact]
    public void Restoring_does_not_check_again()
    {
        var profile = PersonProfile.Restore(
            SubjectId.New(), new string('x', PersonProfile.MaxHeadlineLength + 40),
            "", "", false, SkillList.Empty, Now, Now);

        profile.Headline.Should().HaveLength(PersonProfile.MaxHeadlineLength + 40);
    }
}
