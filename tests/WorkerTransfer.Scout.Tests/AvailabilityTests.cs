using FluentAssertions;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Tests;

/// <summary>
/// Distance is a question between two statements (ADR-0041).
/// </summary>
/// <remarks>
/// <para>What is checked here is not the arithmetic — haversine has stood since
/// ADR-0032 and is measured there. What is checked is <strong>what becomes of
/// it</strong>: three states, no number, and four different ways to the dash,
/// none of which may become a cross.</para>
///
/// <para>The places are real, and the distances are MEASURED on the shipped
/// place table, not estimated: Berlin–Potsdam 27 km, Berlin–Wittenberg 90 km,
/// Berlin–Munich 505 km. The first draft of this suite took Berlin–Leipzig for
/// "between 50 and 100" — it is 150, and the test fell rightly.</para>
/// </remarks>
public class AvailabilityTests
{
    private static readonly PlacePoint? Berlin = PlaceLookup.Find("Berlin");
    private static readonly PlacePoint? Potsdam = PlaceLookup.Find("Potsdam");
    private static readonly PlacePoint? Wittenberg = PlaceLookup.Find("Wittenberg");

    // The place table spells it as it is spelled locally — this is data.
    private static readonly PlacePoint? Munich = PlaceLookup.Find("München");

    /// <summary>The place table knows these four — otherwise nothing here checks anything.</summary>
    [Fact]
    public void The_place_table_knows_the_places_of_this_suite()
    {
        Berlin.Should().NotBeNull();
        Potsdam.Should().NotBeNull();
        Wittenberg.Should().NotBeNull();
        Munich.Should().NotBeNull();

        // And the distances are the ones the cases below compute with. Without
        // these three lines every case below would be an assumption about the
        // place table — and that is exactly where the first draft failed.
        PlaceLookup.DistanceKm(Berlin!.Value, Potsdam!.Value).Should().BeInRange(20, 35);
        PlaceLookup.DistanceKm(Berlin.Value, Wittenberg!.Value).Should().BeInRange(70, 99);
        PlaceLookup.DistanceKm(Berlin.Value, Munich!.Value).Should().BeInRange(450, 550);
    }

    /// <summary>Within the named stage: a yes.</summary>
    [Fact]
    public void Whoever_commutes_far_enough_reaches_the_job() =>
        Availability.Form(CommuteReadiness.UpTo50, Attendance.OnSite, Berlin, Potsdam)
            .State.Should().Be(AvailabilityState.Reachable);

    /// <summary>Beyond it: a no — and the hit stays in the list all the same.</summary>
    /// <remarks>
    /// That it stays is checked by <c>ScoutJourneyTests</c> at the endpoint. Here
    /// it only says that the tick is honest.
    /// </remarks>
    [Fact]
    public void Whoever_wants_to_stay_closer_gets_a_cross() =>
        Availability.Form(CommuteReadiness.UpTo10, Attendance.OnSite, Berlin, Munich)
            .State.Should().Be(AvailabilityState.Further);

    /// <summary>The stage decides, not the distance alone.</summary>
    /// <remarks>
    /// The same route, two statements, two answers — that is the whole idea of
    /// the ADR. Berlin–Wittenberg is 90 km: over 50, under 100.
    /// </remarks>
    [Theory]
    [InlineData(CommuteReadiness.UpTo25, AvailabilityState.Further)]
    [InlineData(CommuteReadiness.UpTo50, AvailabilityState.Further)]
    [InlineData(CommuteReadiness.UpTo100, AvailabilityState.Reachable)]
    [InlineData(CommuteReadiness.AnyDistance, AvailabilityState.Reachable)]
    public void The_same_route_four_statements_two_answers(
        CommuteReadiness commute, AvailabilityState expected) =>
        Availability.Form(commute, Attendance.OnSite, Berlin, Wittenberg)
            .State.Should().Be(expected);

    /// <summary>"Any distance" is a yes without anything being computed.</summary>
    [Fact]
    public void Any_distance_reaches_everything() =>
        Availability.Form(CommuteReadiness.AnyDistance, Attendance.OnSite, Berlin, Munich)
            .State.Should().Be(AvailabilityState.Reachable);

    /// <summary>Remote is a yes, even over five hundred kilometres.</summary>
    /// <remarks>
    /// Whoever does not have to come reaches the job. That is the only exit that
    /// may give a yes without knowing a place — and it may, because then the
    /// question is not asked at all.
    /// </remarks>
    [Fact]
    public void Remote_reaches_everything() =>
        Availability.Form(CommuteReadiness.UpTo10, Attendance.Remote, Berlin, Munich)
            .State.Should().Be(AvailabilityState.Reachable);

    /// <summary>Hybrid counts as on site.</summary>
    /// <remarks>
    /// A decision, not a derivation: at 3+2 somebody travels three days a week.
    /// Halving the distance because it is "only two days of home office" would be
    /// arithmetic about a human being that nobody has justified.
    /// </remarks>
    [Fact]
    public void Hybrid_counts_as_on_site() =>
        Availability.Form(CommuteReadiness.UpTo10, Attendance.Hybrid, Berlin, Munich)
            .State.Should().Be(AvailabilityState.Further);

    // ---------------------------------------------------------------------
    // THE FOUR WAYS TO THE DASH — and none may become a cross.
    // ---------------------------------------------------------------------

    /// <summary>Whoever said nothing gets no "no".</summary>
    /// <remarks>
    /// The core condition: "said nothing" is not "does not fit" (ADR-0022 §3). A
    /// cross here would be a statement about a human being they never made.
    /// </remarks>
    [Fact]
    public void Without_a_statement_by_the_person_there_is_a_dash()
    {
        var tick = Availability.Form(null, Attendance.OnSite, Berlin, Munich);

        tick.State.Should().Be(AvailabilityState.Unsaid);
        tick.State.Should().NotBe(AvailabilityState.Further);
    }

    /// <summary>Without a job there is nothing to compare.</summary>
    [Fact]
    public void Without_a_job_there_is_a_dash() =>
        Availability.Form(CommuteReadiness.UpTo10, null, Berlin, Munich)
            .State.Should().Be(AvailabilityState.Unsaid);

    /// <summary>An unknown place is a gap at OUR end, not a "no" about the person.</summary>
    /// <remarks>
    /// The place table knows DE, AT and CH. Whoever lives elsewhere or spells
    /// their place differently from how it lists it must not get a cross for
    /// that — otherwise our table would be a statement about a human being.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void An_unknown_place_gives_a_dash(bool personKnown, bool jobKnown) =>
        Availability.Form(
                CommuteReadiness.UpTo10,
                Attendance.OnSite,
                personKnown ? Berlin : null,
                jobKnown ? Munich : null)
            .State.Should().Be(AvailabilityState.Unsaid);

    /// <summary>And the dash still carries what was said.</summary>
    /// <remarks>
    /// A dash is no blank: if the person said "up to 10" and only the place is
    /// missing, the card is to be able to show that. Otherwise "we do not know"
    /// would look like "they said nothing".
    /// </remarks>
    [Fact]
    public void The_dash_carries_the_statements_there_are()
    {
        var tick = Availability.Form(
            CommuteReadiness.UpTo10, Attendance.OnSite, null, Munich);

        tick.Commute.Should().Be(CommuteReadiness.UpTo10);
        tick.Attendance.Should().Be(Attendance.OnSite);
    }

    // ---------------------------------------------------------------------

    /// <summary>The words pass through in both directions, unchanged.</summary>
    /// <remarks>
    /// Two spellings for the same stage would be two opportunities to drop a
    /// statement silently — and it would drop out as a dash, not as an error.
    /// </remarks>
    [Theory]
    [InlineData(CommuteReadiness.UpTo10)]
    [InlineData(CommuteReadiness.UpTo25)]
    [InlineData(CommuteReadiness.UpTo50)]
    [InlineData(CommuteReadiness.UpTo100)]
    [InlineData(CommuteReadiness.AnyDistance)]
    public void Every_stage_survives_the_wire(CommuteReadiness commute) =>
        AvailabilityTerms.ReadCommute(AvailabilityTerms.Word(commute)).Should().Be(commute);

    /// <summary>The words on the wire are exactly the ones profile-service writes.</summary>
    /// <remarks>
    /// A round trip alone would stay green if both directions changed together;
    /// the words are a contract with profile-service and <c>web/</c>, so they
    /// stand here as written.
    /// </remarks>
    [Theory]
    [InlineData(CommuteReadiness.UpTo10, "bis_10")]
    [InlineData(CommuteReadiness.UpTo25, "bis_25")]
    [InlineData(CommuteReadiness.UpTo50, "bis_50")]
    [InlineData(CommuteReadiness.UpTo100, "bis_100")]
    [InlineData(CommuteReadiness.AnyDistance, "egal")]
    public void Every_stage_keeps_its_wire_word(CommuteReadiness commute, string word) =>
        AvailabilityTerms.Word(commute).Should().Be(word);

    /// <summary>
    /// And jobs-service's remote mode is read as it is called there.
    /// </summary>
    /// <remarks>
    /// <c>none</c>, <c>hybrid</c>, <c>full</c> — not renamed. A second vocabulary
    /// for the same thing would drift apart at the first new value.
    /// </remarks>
    [Theory]
    [InlineData("none", Attendance.OnSite)]
    [InlineData("hybrid", Attendance.Hybrid)]
    [InlineData("full", Attendance.Remote)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_remote_mode_is_read_as_it_is_called(string? word, Attendance? expected) =>
        AvailabilityTerms.ReadAttendance(word).Should().Be(expected);

    /// <summary>An unknown word becomes a dash, not an error.</summary>
    /// <remarks>
    /// It is a voluntary statement. A failure here would hold up a whole page of
    /// hits because a value was misspelt that nobody had to fill in.
    /// </remarks>
    [Fact]
    public void An_unknown_word_becomes_a_dash() =>
        AvailabilityTerms.ReadCommute("bis_42").Should().BeNull();
}
