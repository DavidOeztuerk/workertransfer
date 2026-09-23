using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Ablage;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Documents;
using WorkerTransfer.Skills;

namespace WorkerTransfer.Resume.Application.Documents;

/// <summary>A document together with what was last read in it.</summary>
/// <param name="Document">The file.</param>
/// <param name="Terms">
/// The terms — or <c>null</c> for "never read". The three states must stay
/// distinguishable: never read, read without text, read without a known word.
/// Merging two of them would tell somebody silently that their reference says
/// nothing (ADR-0022 §3).
/// </param>
public sealed record DocumentState(DocumentRecord Document, DocumentTerms? Terms);

/// <summary>What stands in my documents — without reading anything.</summary>
public sealed record MyTermsQuery(SubjectId Subject) : IQueryMessage<IReadOnlyList<DocumentState>>;

/// <summary>Read my documents. The button.</summary>
/// <remarks>
/// <strong>A command and not a query, because it writes</strong> — the terms
/// are stored, and a query would run past <c>TransactionBehavior</c>, so the row
/// would never be committed (the same trap as <c>POST /me/oauth/start</c> in
/// github-service).
/// </remarks>
public sealed record ReadDocumentsCommand(SubjectId Subject)
    : ICommandMessage<IReadOnlyList<DocumentState>>;

/// <summary>Reads one's own documents — on request, and only then.</summary>
/// <remarks>
/// <para><strong>NEVER AT UPLOAD, and that is half the rule.</strong> "On
/// request" is the other half and toothless on its own: a read that ran along
/// with the upload would formally be "requested" too — by the upload. Something
/// else is meant, and ADR-0004 already says it for GitHub: <em>looking once when
/// asked is something other than watching someone permanently.</em> A human
/// presses a button, reads what was found, and decides. That is why the call
/// stands here and expressly not in <c>AddDocumentCommand</c> — and why
/// <c>RecognitionJourneyTests</c> measures the recogniser's counter after an
/// upload instead of claiming it.</para>
///
/// <para><strong>It reads everything anew every time.</strong> Taking only the
/// unread ones would be thriftier and wrong: the vocabulary grows by pull
/// request, and a reference read before the extension would carry its terms
/// incomplete for ever. The button says "read my documents" and does exactly
/// that.</para>
///
/// <para><strong>The full text stays in this call.</strong> It is not stored,
/// not logged and not returned — only the canonical names the vocabulary
/// recognised go out.</para>
/// </remarks>
public sealed class DocumentTermsList(
    IDocumentRecordStore documents,
    ITermStore terms,
    IDocumentStore documentStore,
    ITextRecogniser recogniser,
    TimeProvider clock) :
    IRequestHandler<MyTermsQuery, IReadOnlyList<DocumentState>>,
    IRequestHandler<ReadDocumentsCommand, IReadOnlyList<DocumentState>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentState>> Handle(
        MyTermsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await State(request.Subject, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentState>> Handle(
        ReadDocumentsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var all = await documents.AllAsync(request.Subject, cancellationToken);
        var previous = await ByDocument(request.Subject, cancellationToken);
        var state = new List<DocumentState>(all.Count);

        foreach (var document in all)
        {
            var content = await documentStore.GetAsync(document.StorageKey, cancellationToken);

            if (content is null)
            {
                // A row without a file. From outside that is the same as "does
                // not exist" — and inventing terms for it would be a statement
                // about a document nobody has any more.
                state.Add(new DocumentState(
                    document, previous.GetValueOrDefault(document.Id)));
                continue;
            }

            var recognised = recogniser.Read(content, document.ContentType);

            var found = DocumentTerms.Record(
                request.Subject,
                document.Id,
                recognised.TextFound,
                // THE VOCABULARY RENAMES AND NEVER INFERS. Found is what it
                // knows: "schweissfachmann" in a reference becomes the name
                // "Schweißfachmann" — a statement about language. What it does
                // not know stays unfound, and that is the boundary and not the
                // gap (ADR-0023).
                TermFinder.Find(recognised.Text),
                clock.GetUtcNow());

            await terms.SaveAsync(found, cancellationToken);
            state.Add(new DocumentState(document, found));
        }

        // THE ANSWER FROM WHAT WAS JUST READ — and expressly not from reading the
        // table anew. `TransactionBehavior` commits only once this handler has
        // returned; a query from here would not yet see its own terms and would
        // answer "nothing found". Measured: the suite was red, the reference was
        // not.
        return state;
    }

    private async Task<IReadOnlyList<DocumentState>> State(
        SubjectId subject, CancellationToken cancellationToken)
    {
        var all = await documents.AllAsync(subject, cancellationToken);
        var byDocument = await ByDocument(subject, cancellationToken);

        return
        [
            .. all.Select(document => new DocumentState(
                document, byDocument.GetValueOrDefault(document.Id)))
        ];
    }

    private async Task<Dictionary<Guid, DocumentTerms>> ByDocument(
        SubjectId subject, CancellationToken cancellationToken)
    {
        var read = await terms.AllAsync(subject, cancellationToken);

        return read
            .GroupBy(entry => entry.DocumentId)
            .ToDictionary(group => group.Key, group => group.First());
    }
}
