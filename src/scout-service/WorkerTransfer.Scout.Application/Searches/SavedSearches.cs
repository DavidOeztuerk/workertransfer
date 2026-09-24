using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Messages;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Application.Searches;

/// <summary>Saves a question — never a result.</summary>
public sealed record SaveSearchCommand(
    TenantId Company,
    SubjectId Subject,
    string Name,
    SearchFilter Filter) : ICommandMessage<Search>;

/// <summary>Stores the filters and nothing else.</summary>
/// <remarks>
/// <para><strong>ADR-0036 decision 4 in one handler.</strong> What arises here
/// is the question: words, location, remote. No hit, no identifier of a human
/// being who was found, no time of a run. A saved result about people goes stale
/// against a revocation — and a revocation has to take effect at the next call,
/// not the one after (ADR-0013).</para>
///
/// <para>A cap per human being, so that saving does not become a store nobody
/// tidies up.</para>
/// </remarks>
public sealed class SaveSearchHandler(ISearchStore store, TimeProvider clock)
    : IRequestHandler<SaveSearchCommand, Search>
{
    /// <inheritdoc />
    /// <exception cref="InputError">The name is missing, too long, or there are too many.</exception>
    public async Task<Search> Handle(
        SaveSearchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await store.ForHumanAsync(
            request.Company, request.Subject, cancellationToken);

        if (existing.Count >= Search.MaxPerPerson)
        {
            throw new InputError(
                $"at most {Search.MaxPerPerson} saved searches per person and company");
        }

        var search = Search.Create(
            request.Company, request.Subject, request.Name, request.Filter, clock.GetUtcNow());

        await store.AppendAsync(search, cancellationToken);

        return search;
    }
}

/// <summary>One's own saved searches in this company.</summary>
public sealed record MySearchesQuery(TenantId Company, SubjectId Subject)
    : IQueryMessage<IReadOnlyList<Search>>;

/// <summary>Reads what this human being saved for this company.</summary>
public sealed class MySearchesHandler(ISearchStore store)
    : IRequestHandler<MySearchesQuery, IReadOnlyList<Search>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Search>> Handle(
        MySearchesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForHumanAsync(request.Company, request.Subject, cancellationToken);
    }
}

/// <summary>Takes a saved search away again.</summary>
public sealed record DeleteSearchCommand(Guid Id, TenantId Company, SubjectId Subject)
    : ICommandMessage<bool>;

/// <summary>Removes it if it belongs to this human being in this company.</summary>
/// <remarks>
/// Both conditions in the store and not here: a handler that first reads and
/// then compares has a gap between the two steps — and an answer that told
/// "not yours" apart from "does not exist" would reveal that a colleague's
/// search exists.
/// </remarks>
public sealed class DeleteSearchHandler(ISearchStore store)
    : IRequestHandler<DeleteSearchCommand, bool>
{
    /// <inheritdoc />
    public Task<bool> Handle(DeleteSearchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.RemoveAsync(
            request.Id, request.Company, request.Subject, cancellationToken);
    }
}
