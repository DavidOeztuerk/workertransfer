using Noelia.Core.Identity;

namespace WorkerTransfer.Messages.Application.Ports;

/// <summary>The ledger did not answer — we simply do not know.</summary>
/// <remarks>
/// Expressly <em>not</em> a <c>false</c>. "I do not know" is something
/// different from "released nothing", and treating the two alike would mean
/// turning an outage into a statement about a human being. The endpoint turns
/// it into 503 — not 404, because that would be the assertion that there is
/// nothing, and not "show", because that would be the opposite one
/// (ADR-0020 §3).
/// </remarks>
/// <param name="reason">The kind of failure, never its content.</param>
public sealed class ConsentGateSilent(string reason) : Exception(reason);

/// <summary>Asks the ledger whether a company may see this human being.</summary>
/// <remarks>
/// <para><strong>Synchronously, per request, without any cache</strong>
/// (ADR-0013). A revocation has to take effect at the next read, not at the one
/// after.</para>
///
/// <para><strong>The existing capability</strong> —
/// <c>profile.visibility:tenant:&lt;id&gt;</c> or
/// <c>profile.visibility:public</c> — and no <c>messages.*</c> of its own: a
/// company may approach whom it may see, and a second capability for the
/// same question would come apart at the first revocation (ADR-0037
/// decision 2).</para>
///
/// <para><strong>It guards only the company's side of an APPROACH.</strong>
/// The person goes on reading what was written to them after they revoked;
/// the company stops at the next request (ADR-0047). An application needs no
/// ledger here: the person applied, and applications-service answers who
/// belongs.</para>
/// </remarks>
public interface IConsentGate
{
    /// <summary>May this company see this human being?</summary>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>The same question for a whole page, in one round.</summary>
    /// <remarks>
    /// As a batch question (ADR-0030): one question per row would be forty
    /// connections for a list of forty cases. The answers come back in the order
    /// of the questions, and a differing length is an <em>error</em> and no
    /// occasion to guess — aligning them wrongly would mean reading the wrong
    /// human being's release.
    /// </remarks>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    Task<IReadOnlyList<bool>> MayEachSeeAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default);
}

/// <summary>The capabilities an approach stands on.</summary>
/// <remarks>
/// Two, and both are <strong>existing</strong> ones. The second is the wider:
/// whoever is publicly visible is visible to this company too — otherwise this
/// service would assert a concealment that does not exist.
/// </remarks>
public static class Visibilities
{
    /// <summary>"To every company" — the switch on the profile page.</summary>
    public const string ProfilePublic = "profile.visibility:public";

    /// <summary>The profile, for this one company.</summary>
    public static string Profile(TenantId company) => $"profile.visibility:tenant:{company.Value}";
}

/// <summary>Writes a release — with the PERSON's own token, never a service's.</summary>
/// <remarks>
/// <para>Exactly one producer: the unsolicited application. Sending it is the
/// person's act, and it releases her profile to that one company — the same
/// as applying to an advert does in applications-service. Nothing else here
/// writes to the ledger.</para>
///
/// <para>The token is hers, so the ledger records the grant as hers. A service
/// secret could not: it would be the platform consenting on somebody's
/// behalf.</para>
/// </remarks>
public interface IReleaseWriter
{
    /// <summary>Releases the person's profile to the company.</summary>
    /// <exception cref="ConsentGateSilent">The ledger does not answer or refuses the call.</exception>
    Task GrantProfileAsync(SubjectId subject, TenantId company, CancellationToken cancellationToken = default);
}
