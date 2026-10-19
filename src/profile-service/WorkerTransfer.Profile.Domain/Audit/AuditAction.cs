namespace WorkerTransfer.Profile.Domain.Audit;

/// <summary>What happened, as the audit trail records it.</summary>
/// <remarks>
/// A closed set and expressly no free-text column: a trail whose vocabulary can
/// grow at the call site cannot be read years later without reading the code
/// that wrote it.
/// <para>
/// <b>There is no action for reading</b>, and that is the one decision of this
/// file. A row "company X looked at person Y" would be exactly the surveillance
/// trail a consent-led system does not want to keep: it would come into being
/// without the person's doing, it would stay with us for years, and it would
/// answer questions nobody may ask. Nor does it have to be reconstructed — a
/// read decides nothing, it only shows what the ledger allowed at the same
/// moment.
/// </para>
/// <para>
/// Every member is stored as the <c>snake_case</c> of its name — that is
/// Npgsql's rule and not ours, and it makes these names Postgres enum labels:
/// <b>renaming a member is a schema change</b>. <b>There is no test for it
/// here</b>, unlike in identity-service (<c>ColumnLabelTests</c>); this comment
/// long claimed one that nobody had written. Whoever adds a fourth member
/// without creating a migration for it therefore notices only at the first
/// insert.
/// </para>
/// </remarks>
public enum AuditAction
{
    /// <summary>Somebody created their profile for the first time.</summary>
    ProfileCreated,

    /// <summary>Somebody changed their profile.</summary>
    ProfileChanged,

    /// <summary>A profile followed the erasure cascade.</summary>
    ProfileDeleted
}
