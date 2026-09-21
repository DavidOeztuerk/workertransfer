using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Nachrichten;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Application.Searches;

/// <summary>Legt eine ResumeRequest ab — nie ein Ergebnis.</summary>
public sealed record SaveSearchCommand(
    TenantId Firma,
    SubjectId Wer,
    string Name,
    SearchFilter Filter) : IBefehl<Search>;

/// <summary>Speichert die Filter und sonst nichts.</summary>
/// <remarks>
/// <para><strong>ADR-0036 Entscheidung 4 in einem Handler.</strong> Was hier
/// entsteht, ist die Frage: Worte, Ort, Remote. Kein Hit, keine Kennung
/// eines gefundenen Menschen, kein Zeitpunkt eines Laufs. Ein gespeichertes
/// Ergebnis über Menschen veraltet gegen einen Revocation — und ein Revocation muss
/// beim nächsten Aufruf wirken, nicht beim übernächsten (ADR-0013).</para>
///
/// <para>Eine Obergrenze je Mensch, damit das Ablegen nicht zu einem Speicher
/// wird, den niemand aufräumt.</para>
/// </remarks>
public sealed class SaveSearchHandler(ISearchStore store, TimeProvider uhr)
    : IRequestHandler<SaveSearchCommand, Search>
{
    /// <inheritdoc />
    /// <exception cref="Eingabefehler">Der Name fehlt, ist zu lang, oder es sind zu viele.</exception>
    public async Task<Search> Handle(
        SaveSearchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bisher = await store.ForHumanAsync(
            request.Firma, request.Wer, cancellationToken);

        if (bisher.Count >= Search.HoechstzahlJeMensch)
        {
            throw new Eingabefehler(
                $"at most {Search.HoechstzahlJeMensch} saved searches per person and company");
        }

        var search = Search.Lege_an(
            request.Firma, request.Wer, request.Name, request.Filter, uhr.GetUtcNow());

        await store.AppendAsync(search, cancellationToken);

        return search;
    }
}

/// <summary>Die eigenen gespeicherten Searches in diesem Unternehmen.</summary>
public sealed record MySearchesQuery(TenantId Firma, SubjectId Wer)
    : IAbfrage<IReadOnlyList<Search>>;

/// <summary>Liest, was dieser Mensch für dieses Unternehmen abgelegt hat.</summary>
public sealed class MySearchesHandler(ISearchStore store)
    : IRequestHandler<MySearchesQuery, IReadOnlyList<Search>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Search>> Handle(
        MySearchesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForHumanAsync(request.Firma, request.Wer, cancellationToken);
    }
}

/// <summary>Nimmt eine gespeicherte Search wieder weg.</summary>
public sealed record DeleteSearchCommand(Guid Id, TenantId Firma, SubjectId Wer) : IBefehl<bool>;

/// <summary>Entfernt sie, wenn sie diesem Menschen in diesem Unternehmen gehört.</summary>
/// <remarks>
/// Beide Bedingungen im Speicher und nicht hier: ein Handler, der erst liest
/// und dann vergleicht, hat zwischen den beiden Schritten eine Lücke — und ein
/// Hit, der „gehört dir nicht" von „gibt es nicht" unterscheidet, verriete,
/// dass die Search eines Kollegen existiert.
/// </remarks>
public sealed class DeleteSearchHandler(ISearchStore store)
    : IRequestHandler<DeleteSearchCommand, bool>
{
    /// <inheritdoc />
    public Task<bool> Handle(DeleteSearchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.RemoveAsync(
            request.Id, request.Firma, request.Wer, cancellationToken);
    }
}
