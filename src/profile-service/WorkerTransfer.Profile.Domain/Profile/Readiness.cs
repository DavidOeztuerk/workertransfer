namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>How far somebody is willing to commute — a stage, not a number.</summary>
/// <remarks>
/// <para><strong>Stages and no kilometre figure</strong> (ADR-0041). A number
/// invites arithmetic — "37 ≤ 50, so 74 % suitable" —, a stage is a statement
/// somebody made. It becomes a tick and never a value.</para>
///
/// <para>The stages are measured, not picked: the average commute is 17.2 km,
/// 7.2 million people commute more than 30 km, 4.1 million more than 50 km,
/// 2.4 million more than 100 km. <see cref="AnyDistance"/> must therefore exist
/// — so that nobody has to make themselves smaller than they are.</para>
///
/// <para>Provenance class <em>named</em> (ADR-0033): the person typed it,
/// nobody derived it.</para>
///
/// <para><strong>The member names are stored values.</strong> The column
/// <c>commute_km</c> holds "Bis10" … "Egal", the names these members had before
/// the code became English; <c>ProfileDbContext</c> writes those strings with an
/// explicit converter. Renaming a member here changes nothing in the database —
/// adding one needs a line there.</para>
/// </remarks>
public enum CommuteReadiness
{
    /// <summary>Up to ten kilometres — below the average.</summary>
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

/// <summary>Whether somebody would move for a job.</summary>
/// <remarks>
/// Three values, and <see cref="Open"/> is not one too many: "it depends" is the
/// most honest answer to this question and must not have to be stored as "no".
/// <para>
/// Stored as "Ja", "Nein" and "Offen" in <c>relocation</c> — see
/// <see cref="CommuteReadiness"/> for why.
/// </para>
/// </remarks>
public enum RelocationReadiness
{
    /// <summary>Yes.</summary>
    Yes,

    /// <summary>No.</summary>
    No,

    /// <summary>It depends.</summary>
    Open
}
