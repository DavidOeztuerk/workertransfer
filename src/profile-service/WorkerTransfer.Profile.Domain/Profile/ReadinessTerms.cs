namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>The words with which a commute stage stands on the wire.</summary>
/// <remarks>
/// <para>The mapping stands in the domain and not at the endpoint, because two
/// services read the same words: profile-service writes them, scout-service
/// reads them through the internal search. Two spellings for the same stage
/// would be two opportunities to drop a statement silently — the same
/// consideration as with the notification kinds.</para>
///
/// <para><strong>The words are German and stay so.</strong> "bis_10" …
/// "egal" are the wire contract with <c>web/</c> and with scout-service; they
/// are values, not identifiers.</para>
///
/// <para><c>null</c> is a valid value on both sides and means "said nothing".
/// It is expressly <em>not</em> an error: a profile without this statement is
/// complete (ADR-0041).</para>
/// </remarks>
public static class CommuteTerms
{
    /// <summary>The word for the stage, or <c>null</c>.</summary>
    public static string? Word(CommuteReadiness? stage) => stage switch
    {
        CommuteReadiness.UpTo10 => "bis_10",
        CommuteReadiness.UpTo25 => "bis_25",
        CommuteReadiness.UpTo50 => "bis_50",
        CommuteReadiness.UpTo100 => "bis_100",
        CommuteReadiness.AnyDistance => "egal",
        _ => null
    };

    /// <summary>The stage for the word, or <c>null</c>.</summary>
    /// <remarks>
    /// An unknown word becomes <c>null</c> and not an error: it is a voluntary
    /// statement, and an unreadable statement is the same as none. A 422 at
    /// this place would hold up a whole form because a value was misspelt that
    /// nobody had to fill in.
    /// </remarks>
    public static CommuteReadiness? Read(string? word) => word switch
    {
        "bis_10" => CommuteReadiness.UpTo10,
        "bis_25" => CommuteReadiness.UpTo25,
        "bis_50" => CommuteReadiness.UpTo50,
        "bis_100" => CommuteReadiness.UpTo100,
        "egal" => CommuteReadiness.AnyDistance,
        _ => null
    };

    /// <summary>
    /// How many kilometres the stage covers — <c>null</c> means "without a limit".
    /// </summary>
    /// <remarks>
    /// <strong>This number never leaves the service.</strong> It stands here
    /// because somewhere "up to 50" and a distance have to become a yes or a
    /// no, and it is read at exactly one place: when the tick is formed. It
    /// belongs in no contract, in no answer and in no ordering (ADR-0041).
    /// </remarks>
    public static int? KilometreLimit(CommuteReadiness stage) => stage switch
    {
        CommuteReadiness.UpTo10 => 10,
        CommuteReadiness.UpTo25 => 25,
        CommuteReadiness.UpTo50 => 50,
        CommuteReadiness.UpTo100 => 100,
        _ => null
    };
}

/// <summary>The words for the readiness to relocate.</summary>
/// <remarks>
/// "ja", "nein" and "offen" are wire values, like the commute words above.
/// </remarks>
public static class RelocationTerms
{
    /// <summary>The word, or <c>null</c>.</summary>
    public static string? Word(RelocationReadiness? choice) => choice switch
    {
        RelocationReadiness.Yes => "ja",
        RelocationReadiness.No => "nein",
        RelocationReadiness.Open => "offen",
        _ => null
    };

    /// <summary>The choice for the word, or <c>null</c>.</summary>
    public static RelocationReadiness? Read(string? word) => word switch
    {
        "ja" => RelocationReadiness.Yes,
        "nein" => RelocationReadiness.No,
        "offen" => RelocationReadiness.Open,
        _ => null
    };
}
