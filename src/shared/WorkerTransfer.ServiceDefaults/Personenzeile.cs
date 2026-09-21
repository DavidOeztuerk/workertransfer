namespace WorkerTransfer.ServiceDefaults;

/// <summary>"This row is about a human being."</summary>
/// <remarks>
/// Mostly that can be seen from the column: <c>subject_id</c> or
/// <c>user_id</c>. With some aggregates the key <strong>is</strong> the person,
/// though — <c>profiles.id</c>, <c>portfolios.id</c>,
/// <c>github_connections.id</c> —, and then the column is simply called
/// <c>id</c>.
/// <para>
/// A check that looks only for column names misses exactly those tables, and
/// with them whole services in the erasure cascade (ADR-0027 §4). So it is
/// <em>said</em> rather than guessed, and said where the table comes into
/// being.
/// </para>
/// </remarks>
public static class SubjectKeyedTable
{
    /// <summary>The name of the annotation on the entity type.</summary>
    public const string Anmerkung = "WorkerTransfer:Personenzeile";
}
