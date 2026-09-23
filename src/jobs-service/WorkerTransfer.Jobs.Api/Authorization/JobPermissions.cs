namespace WorkerTransfer.Jobs.Api.Authorization;

/// <summary>The permissions this service knows.</summary>
/// <remarks>
/// <para><strong>The line lies at the notice board, not at the text.</strong>
/// <em>Writing</em> and changing an advert is the work somebody is invited
/// for — as long as it is a draft, it faces nobody. <em>Publishing</em> and
/// <em>closing</em> are the two moments in which the advert represents the
/// company to the outside: the one puts it up, the other takes it down — and
/// with it the chance to apply.</para>
///
/// <para>"A <c>member</c> cannot delete my jobs" means exactly
/// <see cref="Close"/> in this domain: an advert is never deleted, it is
/// closed.</para>
///
/// <para>The names say <em>what</em> an endpoint wants to be permitted for,
/// not <em>who</em> that is — who it is, is decided by
/// <c>ServiceDefaults.Roles.AdminPermission</c> from identity-service's
/// membership table.</para>
/// </remarks>
public static class JobPermissions
{
    /// <summary>Publishing an advert.</summary>
    public const string Publish = "jobs.publish";

    /// <summary>Closing an advert.</summary>
    public const string Close = "jobs.close";
}
