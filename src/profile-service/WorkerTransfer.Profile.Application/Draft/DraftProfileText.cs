using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Messages;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Draft;

/// <summary>Asks for a draft of one's own profile text.</summary>
/// <param name="Subject">The person from the token.</param>
/// <param name="Wish">What they want different.</param>
/// <remarks>
/// An <see cref="IQueryMessage{TResponse}"/> and expressly no command: nothing
/// is written, so no transaction may open either. The draft lives in the form
/// until the person saves it — and then it is their text.
/// </remarks>
public sealed record DraftQuery(SubjectId Subject, string Wish) : IQueryMessage<string>;

/// <summary>Builds the prompt from the STORED profile and asks the provider.</summary>
/// <remarks>
/// From the stored profile, not from the request: what the caller cannot send,
/// they cannot smuggle into a foreign service. The wish is the only line they
/// contribute, and it is addressed to themselves.
/// <para>
/// Nothing is stored — not the prompt, not the answer. No entry in the audit
/// trail: a row "asked for help" would be a statement about a human being that
/// nobody needs and that outlives them.
/// </para>
/// </remarks>
public sealed class DraftHandler(IProfileStore store, IDrafter drafter)
    : IRequestHandler<DraftQuery, string>
{
    /// <inheritdoc />
    /// <exception cref="DraftUnavailable">No provider, or it is silent.</exception>
    public async Task<string> Handle(DraftQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await store.GetAsync(request.Subject, cancellationToken);

        var situation = new DraftSituation(
            profile?.Headline ?? string.Empty,
            profile?.Text ?? string.Empty,
            profile?.Skills.Values ?? [],
            request.Wish);

        return await drafter.DraftAsync(situation, request.Subject, cancellationToken);
    }
}
