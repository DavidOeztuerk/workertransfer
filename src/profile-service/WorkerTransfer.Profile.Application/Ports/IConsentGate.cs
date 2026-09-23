using Noelia.Core.Identity;

namespace WorkerTransfer.Profile.Application.Ports;

/// <summary>The ledger did not answer — we simply do not know.</summary>
/// <remarks>
/// Expressly no input error and no domain outcome, but a system state. The
/// endpoint turns it into 503: showing neither 404 nor the profile, because
/// both would be a claim about a human being that nobody can make at this
/// moment (ADR-0020 §3).
/// </remarks>
/// <param name="message">What went wrong — never what it was about.</param>
public sealed class ConsentGateSilent(string message) : Exception(message);

/// <summary>The capabilities a profile is about.</summary>
/// <remarks>
/// Two, and both are <em>visibilities</em>. That is the reason why "delete
/// <c>profile.visibility:public</c>" can never mean "delete the profile"
/// (ADR-0027 §1): a revocation of the visibility is no erasure order.
/// </remarks>
public static class Visibilities
{
    /// <summary>"To every company" — the switch on the profile page.</summary>
    public const string Public = "profile.visibility:public";

    /// <summary>"To this one company" — comes into being with an application.</summary>
    /// <remarks>
    /// The additive refinement from ADR-0020: <c>:public</c> stays what it was,
    /// and here a second possibility is added — no loosening of the first.
    /// </remarks>
    /// <param name="company">The company from the caller's token.</param>
    public static string ForCompany(TenantId company) => $"profile.visibility:tenant:{company.Value}";
}

/// <summary>May the caller see this person's profile?</summary>
/// <remarks>
/// A port and not an import: the handlers are to know <em>that</em> they have
/// to ask, not <em>how</em>. The HTTP adapter lies in the infrastructure, and so
/// does what the question is authenticated with — the service asks on behalf of
/// the caller, not with an account of its own.
/// <para>
/// Asked at every foreign read, synchronously and without any cache
/// (ADR-0013). A revocation must take effect at the next read, not the one after
/// — a cache would be no performance detail here but a breach of the rule.
/// </para>
/// </remarks>
public interface IConsentGate
{
    /// <summary>The question for one human being.</summary>
    /// <remarks>
    /// <strong>Only the single question now.</strong> Beside it stood a batch
    /// question, and it stood there for <c>GET /candidates</c>: a page of
    /// twenty profiles would otherwise have cost up to forty single calls. That
    /// route fell on 11.09.2026, and the batch question with it — scout-service
    /// asks in batches now, for its own page. Two batch questions to the same
    /// ledger would be two places where visibility is decided.
    /// </remarks>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);
}
