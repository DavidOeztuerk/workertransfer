using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Nachrichten;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>Several "may I?" in one round trip.</summary>
/// <remarks>
/// The same question as <see cref="CheckConsentQuery"/>, <em>n</em>
/// times: same access, same synchronous read, same absence of a cache and the
/// same silence about the withdrawal reason (ADR-0030). What gets cheaper is
/// the number of round trips, not the access.
/// <para>
/// The ceiling lives in the boundary contract, not here — two truths about what
/// one request may carry would be one too many.
/// </para>
/// </remarks>
public sealed record BatchCheckQuery(IReadOnlyList<CheckConsentQuery> Pairs)
    : IQuery<BatchCheckResult>;

/// <summary>Reads every pair in one query, answers in the order asked.</summary>
public sealed class BatchCheckHandler(IConsentLedger ledger)
    : IRequestHandler<BatchCheckQuery, BatchCheckResult>
{
    /// <inheritdoc />
    public async Task<BatchCheckResult> Handle(
        BatchCheckQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pairs = new List<(SubjectId Subject, Capability Capability)>(request.Pairs.Count);

        foreach (var question in request.Pairs)
        {
            if (!Capability.TryParse(question.Capability, out var skill))
            {
                return new BatchCheckResult.Malformed("capability");
            }

            pairs.Add((new SubjectId(question.Subject), skill));
        }

        var latest = await ledger.LatestAsync(pairs, cancellationToken);

        // In the order of the questions, one per pair. The caller maps the
        // answers onto its rows; in any other order it would have to match on
        // (subject, capability), and a pair asked twice would be ambiguous.
        var states = new List<ConsentState>(pairs.Count);

        foreach (var (subject, skill) in pairs)
        {
            states.Add(latest.TryGetValue((subject.Value, skill.Value), out var consentEvent)
                ? Projection.State([consentEvent])
                : Projection.State([]));
        }

        return new BatchCheckResult.States(states);
    }
}
