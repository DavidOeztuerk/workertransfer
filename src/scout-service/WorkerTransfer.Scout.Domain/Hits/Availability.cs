using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Domain.Hits;

/// <summary>How far somebody is willing to commute — the stage, never a number.</summary>
/// <remarks>
/// The same five stages as on the profile, and for the same reason: a number
/// invites arithmetic, a stage is a statement. It comes in through the internal
/// search as a word and is read here, not computed.
/// </remarks>
public enum CommuteReadiness
{
    /// <summary>Up to ten kilometres.</summary>
    UpTo10,

    /// <summary>Up to twenty-five.</summary>
    UpTo25,

    /// <summary>Up to fifty.</summary>
    UpTo50,

    /// <summary>Up to a hundred.</summary>
    UpTo100,

    /// <summary>Distance does not matter.</summary>
    AnyDistance
}

/// <summary>Which attendance a job demands.</summary>
/// <remarks>
/// <para><strong>The same three-way split jobs-service has always carried as
/// <c>RemoteMode</c></strong> — and exactly for that reason no second field
/// arises there. ADR-0041 §2 asked for "a field on the advert:
/// remote / hybrid / vor_ort"; it already existed, under another name and with
/// the same reasoning in the source: <em>"hybrid is the commonest case and not
/// an intermediate step of true"</em>. A second one would have been a second
/// truth about the same question.</para>
/// </remarks>
public enum Attendance
{
    /// <summary>Fully remote.</summary>
    Remote,

    /// <summary>Partly on site — the commonest case (3+2).</summary>
    Hybrid,

    /// <summary>Fully on site.</summary>
    OnSite
}

/// <summary>What can be said about availability — three states.</summary>
/// <remarks>
/// <para><strong>The third is as important as the first two.</strong> Whoever
/// said nothing gets no "no": "said nothing" is not "does not fit" (ADR-0041,
/// ADR-0022 §3). The same holds when we cannot resolve the location — then the
/// information is missing at <em>our</em> end, and that is even less a statement
/// about the human being.</para>
/// </remarks>
public enum AvailabilityState
{
    /// <summary>The job lies within what the person said.</summary>
    Reachable,

    /// <summary>It lies beyond — and the hit stays in the list all the same.</summary>
    Further,

    /// <summary>Nobody said anything, or a location is unknown.</summary>
    Unsaid
}

/// <summary>
/// The tick for the distance — and everything that may stand beside it.
/// </summary>
/// <param name="State">Yes, no or dash.</param>
/// <param name="Commute">What the person said, or <c>null</c>.</param>
/// <param name="Attendance">What the job demands, or <c>null</c>.</param>
/// <remarks>
/// <para><strong>No kilometre figure.</strong> The distance is computed and
/// reduced at once to one of the three states; it stands in no field of this
/// record and never travels into a contract. A number here would look like a
/// measurement but would be the distance between two town centres — and as soon
/// as two of them stand one below the other, it ranks people (ADR-0022).</para>
///
/// <para>What stands beside it are the two <em>statements</em>: "says: up to
/// 50 km" and "job: hybrid". Somebody made both, and both can be contradicted —
/// unlike a number.</para>
/// </remarks>
public sealed record Availability(
    AvailabilityState State,
    CommuteReadiness? Commute,
    Attendance? Attendance)
{
    /// <summary>When there is nothing to say.</summary>
    public static Availability Unsaid { get; } =
        new(AvailabilityState.Unsaid, null, null);

    /// <summary>
    /// Forms the tick from two statements and two places.
    /// </summary>
    /// <remarks>
    /// <para><strong>The order of the exits is the statement.</strong> There are
    /// four ways to <see cref="AvailabilityState.Unsaid"/>, and none of them may
    /// become a "no": no stage named, no job named, a location that cannot be
    /// resolved — and the special case that the job is fully remote.</para>
    ///
    /// <para><strong>Remote is a yes, not a dash</strong>: whoever does not have
    /// to come reaches the job, however far away they live. That is the only exit
    /// that may give a <em>yes</em> without knowing a location.</para>
    ///
    /// <para><strong>Hybrid counts as on site</strong>, and that is a decision:
    /// at 3+2 somebody travels three days a week. Halving the distance because it
    /// is "only two days of home office" would be arithmetic about a human being
    /// that nobody has justified.</para>
    /// </remarks>
    /// <param name="commute">What the person said, or <c>null</c>.</param>
    /// <param name="attendance">What the job demands, or <c>null</c>.</param>
    /// <param name="atThePerson">The person's resolved place, or <c>null</c>.</param>
    /// <param name="atTheJob">The job's resolved place, or <c>null</c>.</param>
    public static Availability Form(
        CommuteReadiness? commute,
        Attendance? attendance,
        PlacePoint? atThePerson,
        PlacePoint? atTheJob)
    {
        if (attendance is null)
        {
            return Unsaid;
        }

        if (attendance is Hits.Attendance.Remote)
        {
            // Whoever does not have to come reaches the job. The person's stage
            // stands beside it all the same — it is information, not a proviso.
            return new Availability(
                AvailabilityState.Reachable, commute, attendance);
        }

        if (commute is null)
        {
            return new Availability(AvailabilityState.Unsaid, null, attendance);
        }

        if (commute is CommuteReadiness.AnyDistance)
        {
            return new Availability(
                AvailabilityState.Reachable, commute, attendance);
        }

        if (atThePerson is not { } person || atTheJob is not { } job)
        {
            // An unknown place is a gap at OUR end. Turning it into a cross would
            // mean declaring our place table a statement about a human being.
            return new Availability(AvailabilityState.Unsaid, commute, attendance);
        }

        var limit = KilometreLimit(commute.Value);
        var distance = PlaceLookup.DistanceKm(person, job);

        return new Availability(
            distance <= limit
                ? AvailabilityState.Reachable
                : AvailabilityState.Further,
            commute,
            attendance);
    }

    /// <summary>
    /// How many kilometres a stage covers.
    /// </summary>
    /// <remarks>
    /// <strong>Private, and that is the point.</strong> This number is read at
    /// exactly one place — in the comparison a few lines further up — and never
    /// leaves the type. It belongs in no contract, in no answer and in no
    /// ordering (ADR-0041).
    /// </remarks>
    private static double KilometreLimit(CommuteReadiness commute) => commute switch
    {
        CommuteReadiness.UpTo10 => 10,
        CommuteReadiness.UpTo25 => 25,
        CommuteReadiness.UpTo50 => 50,
        CommuteReadiness.UpTo100 => 100,
        _ => double.MaxValue
    };
}

/// <summary>The words with which the stages and the attendance stand on the wire.</summary>
/// <remarks>
/// <para>The same words profile-service writes and jobs-service reports. Two
/// spellings for the same stage would be two opportunities to drop a statement
/// silently — and it would drop out as a dash, not as an error.</para>
///
/// <para><strong>The words are German where they are, and stay so.</strong>
/// "bis_10" … "egal" and "vor_ort" are the wire contract with <c>web/</c> and
/// with profile-service; they are values, not identifiers.</para>
/// </remarks>
public static class AvailabilityTerms
{
    /// <summary>The stage for the word, or <c>null</c>.</summary>
    public static CommuteReadiness? ReadCommute(string? word) => word switch
    {
        "bis_10" => CommuteReadiness.UpTo10,
        "bis_25" => CommuteReadiness.UpTo25,
        "bis_50" => CommuteReadiness.UpTo50,
        "bis_100" => CommuteReadiness.UpTo100,
        "egal" => CommuteReadiness.AnyDistance,
        _ => null
    };

    /// <summary>The word for the stage, for the answer.</summary>
    public static string? Word(CommuteReadiness? commute) => commute switch
    {
        CommuteReadiness.UpTo10 => "bis_10",
        CommuteReadiness.UpTo25 => "bis_25",
        CommuteReadiness.UpTo50 => "bis_50",
        CommuteReadiness.UpTo100 => "bis_100",
        CommuteReadiness.AnyDistance => "egal",
        _ => null
    };

    /// <summary>
    /// The attendance for jobs-service's word, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <c>none</c>, <c>hybrid</c>, <c>full</c> — that is how <c>JobV1</c> reports
    /// the <c>RemoteMode</c>, and that is how it is read here. The words are NOT
    /// renamed: a second vocabulary for the same thing would drift apart at the
    /// first new value.
    /// </remarks>
    public static Attendance? ReadAttendance(string? word) => word switch
    {
        "none" => Attendance.OnSite,
        "hybrid" => Attendance.Hybrid,
        "full" => Attendance.Remote,
        _ => null
    };

    /// <summary>The word for the attendance, for the answer.</summary>
    public static string? Word(Attendance? attendance) => attendance switch
    {
        Attendance.OnSite => "vor_ort",
        Attendance.Hybrid => "hybrid",
        Attendance.Remote => "remote",
        _ => null
    };

    /// <summary>The word for the state, for the answer.</summary>
    public static string Word(AvailabilityState state) => state switch
    {
        AvailabilityState.Reachable => "reachable",
        AvailabilityState.Further => "further",
        AvailabilityState.Unsaid => "unsaid",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
