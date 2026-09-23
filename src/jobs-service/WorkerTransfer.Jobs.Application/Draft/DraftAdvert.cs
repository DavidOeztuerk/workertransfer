using MediatR;
using WorkerTransfer.Jobs.Application.Messages;
using WorkerTransfer.Jobs.Application.Ports;

namespace WorkerTransfer.Jobs.Application.Draft;

/// <summary>Helps a company phrase its own advert.</summary>
/// <remarks>
/// The one company agent that can be built without a weighing of its own: it
/// never says anything <em>about</em> anybody.
/// <para>
/// Nothing is stored — not the prompt, not the answer. No memory, no vector
/// store, no plan-act-reflect, no entry in the consent ledger: the button is the
/// consent, informed and per use (ADR-0024).
/// </para>
/// </remarks>
public sealed record DraftAdvertQuery(
    string Title,
    string Description,
    IReadOnlyList<string> Skills,
    string Location,
    string Wish) : IQueryMessage<string>;

/// <inheritdoc cref="DraftAdvertQuery" />
public sealed class DraftAdvertHandler(IDrafter drafter)
    : IRequestHandler<DraftAdvertQuery, string>
{
    /// <inheritdoc />
    /// <exception cref="DraftUnavailable">No provider, or it stays silent.</exception>
    public Task<string> Handle(
        DraftAdvertQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Without tenant_id and without company name — the context class has no
        // fields for them, so nothing can slip in either.
        return drafter.DraftAsync(
            new AdvertDraft(
                request.Title, request.Description, request.Skills,
                request.Location, request.Wish),
            cancellationToken);
    }
}
