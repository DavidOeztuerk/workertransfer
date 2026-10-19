using Noelia.Core.Identity;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>The ledger did not answer.</summary>
/// <remarks>
/// Expressly <em>no</em> <c>false</c>. "I do not know" is something other than
/// "no", and treating the two alike would mean turning an outage into a
/// statement about a human being. The endpoint makes 503 out of it.
/// </remarks>
/// <param name="reason">The kind of failure, never its content.</param>
public sealed class ConsentGateSilent(string reason) : Exception(reason);

/// <summary>The capabilities a search is about.</summary>
/// <remarks>
/// The same words as in profile-service. A second spelling would be a second
/// truth about what somebody released — and its silent variant shows profiles
/// nobody released.
/// </remarks>
public static class Visibilities
{
    /// <summary>"For all companies" — the switch on the profile page.</summary>
    public const string Public = "profile.visibility:public";

    /// <summary>"For this one company".</summary>
    public static string ForCompany(TenantId company) => $"profile.visibility:tenant:{company.Value}";
}

/// <summary>May this company see this human being?</summary>
/// <remarks>
/// <para>Synchronous, per call, <strong>without any cache</strong> (ADR-0013).
/// A revocation has to take effect at the next read, not the one after — and
/// that is exactly why this service stores no result but only the question
/// (ADR-0036 decision 4).</para>
///
/// <para>One batch question for the whole page (ADR-0030): one question per row
/// would be up to a hundred connection set-ups for one view. The answers come in
/// the order of the questions, and a differing length is an <em>error</em> and
/// no occasion to guess — assigning wrongly would mean showing the profile of
/// the wrong person.</para>
/// </remarks>
public interface IConsentGate
{
    /// <summary>One answer per human being, in the order of the questions.</summary>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    Task<IReadOnlyList<bool>> MaySeeAllAsync(
        IReadOnlyList<SubjectId> subjects,
        TenantId company,
        CancellationToken cancellationToken = default);
}
