using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Messages;

namespace WorkerTransfer.Scout.Application.Candidates;

/// <summary>"Tell these human beings that their profile was discovered."</summary>
/// <param name="Subjects">The human beings of one page of hits.</param>
public sealed record ReportDiscoveryCommand(IReadOnlyList<SubjectId> Subjects) : ICommandMessage<int>;

/// <summary>Notes one intent per human being in the outbox. Nothing more.</summary>
/// <remarks>
/// <para><strong>The fifth decision of ADR-0036 and the notice of
/// ADR-0033.</strong> The draft once wanted <c>GET /me/scouting</c> — a table
/// "who looked at whom". It does not arise here and is not to arise: it would
/// be the most sensitive table of the system. Instead a <em>message</em>, as in
/// a social network.</para>
///
/// <para><strong>The outbox stays free of content</strong> (ADR-0025): an
/// identifier and a kind, never a content. The message therefore names
/// <strong>no company</strong> — "a company" is enough. Who is searching is a
/// statement about the company, and the person can do nothing with it as long
/// as nobody has approached them. Consistently, the tenant does not appear in
/// this command at all.</para>
///
/// <para><strong>At most one per person and day</strong> — and that cap lies
/// with notification-service, not here. That is deliberate: delivery is
/// at-least-once, so a second row is possible at any time, and a cap at the
/// origin would be a table "this person was reported on …" — again a register
/// of a human being's visibility. The recipient can answer the same promise
/// from its own inbox without a new row arising anywhere.</para>
///
/// <para>Deduplicated all the same, within this one call: the same human being
/// is not to be noted twice for one page.</para>
/// </remarks>
public sealed class ReportDiscoveryHandler(IOutbox outbox)
    : IRequestHandler<ReportDiscoveryCommand, int>
{
    /// <summary>The word on the wire. The same notification-service reads.</summary>
    /// <remarks>
    /// Two spellings for the same kind would be two opportunities to drop a
    /// message silently.
    /// </remarks>
    public const string Kind = "profile_discovered";

    /// <inheritdoc />
    /// <returns>How many intents were noted.</returns>
    public async Task<int> Handle(
        ReportDiscoveryCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var noted = 0;

        foreach (var subject in request.Subjects.Distinct())
        {
            await outbox.NoteAsync(subject, Kind, cancellationToken);
            noted++;
        }

        return noted;
    }
}
