using Noelia.Core.Identity;
using WorkerTransfer.Scout.Domain.Hits;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>What evidence there is about a human being.</summary>
/// <param name="Evidence">Topics and language names with project and link.</param>
/// <param name="State">What holds about the completeness of this list.</param>
public sealed record EvidenceSheet(IReadOnlyList<Evidence> Evidence, EvidenceState State);

/// <summary>Fetches evidence to a hit — never to find it.</summary>
/// <remarks>
/// <para><strong>The third condition of ADR-0036 stands in the order of the
/// calls.</strong> The search runs through <see cref="IProfileSearch"/> and
/// exclusively over <em>named</em> words; this port is called only when the
/// hits are settled and the release is checked. Evidence may therefore adorn a
/// human being but never make them findable: a topic is a statement about a
/// repository, and whoever searched by it would silently turn it into one about
/// the human being (ADR-0033).</para>
///
/// <para>The release of the evidence is a separate one
/// (<c>github.visibility:public</c>) and is checked by github-service itself.
/// This service asks on the caller's behalf and gets nothing that is not
/// released.</para>
///
/// <para>A failure is <strong>no failure of the search</strong>: the hit stays,
/// and its <see cref="EvidenceState"/> says that something is missing here.
/// Whoever has nothing on GitHub is not worse but elsewhere (ADR-0022 §3) — and
/// whoever is not answering right now says nothing at all about the person.</para>
/// </remarks>
public interface IEvidenceSource
{
    /// <summary>The evidence about a human being. Does not throw.</summary>
    Task<EvidenceSheet> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
