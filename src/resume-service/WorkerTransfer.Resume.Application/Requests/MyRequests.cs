using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Requests;

namespace WorkerTransfer.Resume.Application.Requests;

/// <summary>One request as the person sees it — the proceeding and what holds.</summary>
/// <param name="Request">What happened.</param>
/// <param name="Active">
/// What holds, freshly from the ledger.
/// </param>
/// <remarks>
/// The two can disagree, and that is the point: after a withdrawal
/// <see cref="RequestState.Granted"/> stays and <paramref name="Active"/> falls
/// to <c>false</c>. Exactly why the permission is not a field on the
/// proceeding.
/// </remarks>
public sealed record ResumeRequestView(ResumeRequest Request, bool Active);

/// <summary>Who asked about me, and what they hold right now.</summary>
public sealed record MyRequestsQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<ResumeRequestView>>;

/// <summary>Reads the proceedings, then asks the ledger about the granted ones.</summary>
/// <remarks>
/// Only for granted ones: for pending and declined the answer is settled, and
/// every question costs a round trip. The granted ones go out as one batch —
/// the Python service made one call per row and paid for a person with forty
/// requests in seconds (ADR-0030).
/// </remarks>
public sealed class MyRequestsHandler(IRequestStore store, IConsentGate gate)
    : IRequestHandler<MyRequestsQuery, IReadOnlyList<ResumeRequestView>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeRequestView>> Handle(
        MyRequestsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requests = await store.ForPersonAsync(request.Subject, cancellationToken);
        var granted = requests.Where(r => r.State == RequestState.Granted).ToList();

        var verdicts = granted.Count == 0
            ? []
            : await gate.MayEachReadResumeAsync(
                [.. granted.Select(r => (r.Subject, r.Company))], cancellationToken);

        var activeById = new Dictionary<Guid, bool>();

        for (var i = 0; i < granted.Count; i++)
        {
            activeById[granted[i].Id] = verdicts[i];
        }

        return [.. requests.Select(r => new ResumeRequestView(
            r, activeById.TryGetValue(r.Id, out var active) && active))];
    }
}
