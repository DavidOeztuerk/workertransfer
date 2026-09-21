namespace WorkerTransfer.Skills;

/// <summary>The one number two services have to agree on.</summary>
/// <remarks>
/// <c>profile-service</c> and <c>jobs-service</c> each keep their own skill
/// list — the sharing rule wants it that way, because how many entries a list
/// carries is a decision of the service that owns it. A profile holds thirty, a
/// job twenty, and that is fine.
/// <para>
/// <b>The length is not.</b> The comparison happens in the browser: if an
/// advert were allowed to name a requirement longer than a person can enter at
/// all, it would be guaranteed never to be a hit — a line in the list that can
/// never become a tick for anybody.
/// </para>
/// <para>
/// The Python service held that together with a test importing both constants.
/// Here it is <em>one</em> constant: the two can no longer drift apart, because
/// there is nothing that could drift.
/// </para>
/// </remarks>
public static class SkillLimits
{
    /// <summary>How long a single skill may be.</summary>
    public const int Hoechstlaenge = 50;
}
