using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.DocumentStore;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Documents;
using WorkerTransfer.Resume.Domain.Resumes;

namespace WorkerTransfer.Resume.Application.Documents;

/// <summary>How accepting a document ended.</summary>
public abstract record DocumentResult
{
    private DocumentResult()
    {
    }

    /// <summary>Accepted.</summary>
    public sealed record Done(DocumentRecord Document) : DocumentResult;

    /// <summary>Something is wrong with the file.</summary>
    public sealed record Rejected(string Reason) : DocumentResult;

    /// <summary>There are enough already.</summary>
    public sealed record TooMany : DocumentResult;
}

/// <summary>Enclose a document.</summary>
/// <param name="Content">The bytes. What they are is decided by their signature.</param>
public sealed record AddDocumentCommand(
    SubjectId Subject,
    string? Name,
    DocumentKind Kind,
    ReadOnlyMemory<byte> Content) : ICommandMessage<DocumentResult>;

/// <summary>One's own documents.</summary>
public sealed record MyDocumentsQuery(SubjectId Subject) : IQueryMessage<IReadOnlyList<DocumentRecord>>;

/// <summary>The content of a document, together with its row.</summary>
public sealed record DocumentContentQuery(SubjectId Subject, Guid Id)
    : IQueryMessage<(DocumentRecord Document, byte[] Content)?>;

/// <summary>Take a document away again.</summary>
public sealed record DeleteDocumentCommand(SubjectId Subject, Guid Id) : ICommandMessage<bool>;

/// <summary>This file is the CV — the previous CV file no longer is.</summary>
public sealed record MarkDocumentAsResumeCommand(SubjectId Subject, Guid Id) : ICommandMessage<bool>;

/// <summary>Choose the template of one's own résumé.</summary>
public sealed record ChooseTemplateCommand(SubjectId Subject, ResumeTemplate Template)
    : ICommandMessage<bool>;

/// <summary>Keeps the documents.</summary>
public sealed class DocumentCommands(
    IDocumentRecordStore store,
    IResumeStore resumes,
    ITermStore terms,
    IDocumentStore documentStore,
    TimeProvider clock) :
    IRequestHandler<AddDocumentCommand, DocumentResult>,
    IRequestHandler<MyDocumentsQuery, IReadOnlyList<DocumentRecord>>,
    IRequestHandler<DocumentContentQuery, (DocumentRecord Document, byte[] Content)?>,
    IRequestHandler<DeleteDocumentCommand, bool>,
    IRequestHandler<MarkDocumentAsResumeCommand, bool>,
    IRequestHandler<ChooseTemplateCommand, bool>
{
    /// <inheritdoc />
    public async Task<DocumentResult> Handle(
        AddDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // THE TYPE COMES FROM THE BYTES. A `Content-Type` and an extension are
        // both freely chosen; whoever believes them accepts an executable
        // because somebody named it `reference.png`.
        var type = ContentTypeDetection.Detect(request.Content.Span);

        if (type is null)
        {
            return new DocumentResult.Rejected(
                "only PNG, JPEG and PDF are accepted");
        }

        var existing = await store.AllAsync(request.Subject, cancellationToken);

        if (request.Kind == DocumentKind.CurriculumVitae)
        {
            foreach (var old in existing.Where(d => d.Kind == DocumentKind.CurriculumVitae))
            {
                await store.DeleteAsync(request.Subject, old.Id, cancellationToken);
                await terms.DeleteAsync(request.Subject, old.Id, cancellationToken);
                await documentStore.DeleteAsync(old.StorageKey, cancellationToken);
            }

            existing = await store.AllAsync(request.Subject, cancellationToken);
        }

        if (existing.Count >= DocumentRecord.MaxDocuments)
        {
            return new DocumentResult.TooMany();
        }

        DocumentRecord document;

        try
        {
            document = DocumentRecord.Accept(
                request.Subject, request.Name, request.Kind, type,
                request.Content.Length, clock.GetUtcNow());
        }
        catch (DocumentError error)
        {
            return new DocumentResult.Rejected(error.Message);
        }

        // FIRST THE BYTES, THEN THE ROW. The other way round the list would show
        // an entry whose content never existed — and a click on it would run
        // into nothing, without anybody being able to say why. A file without a
        // row, on the other hand, is only space a clean-up finds.
        await documentStore.PutAsync(document.StorageKey, request.Content, cancellationToken);
        await store.SaveAsync(document, cancellationToken);

        return new DocumentResult.Done(document);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentRecord>> Handle(
        MyDocumentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.AllAsync(request.Subject, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(DocumentRecord Document, byte[] Content)?> Handle(
        DocumentContentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = await store.GetAsync(request.Subject, request.Id, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var content = await documentStore.GetAsync(document.StorageKey, cancellationToken);

        // A row without a file: from outside the same as "does not exist".
        // Answering anything else would mean talking about an error of our own
        // where the caller gains nothing from it.
        return content is null ? null : (document, content);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = await store.GetAsync(request.Subject, request.Id, cancellationToken);

        if (document is null)
        {
            return false;
        }

        // FIRST THE ROW, THEN THE BYTES — exactly the other way round from
        // creating. If it breaks off in between, a file without a row remains:
        // unreachable and cleanable. The other way round a row without a file
        // would remain and would look to the person like a document that still
        // exists.
        await store.DeleteAsync(request.Subject, request.Id, cancellationToken);

        // AND ITS TERMS. What was read from this file is a statement about it —
        // without it, it has no subject any more. If they stayed, the profile
        // page would keep offering words from a reference the person has just
        // taken away (ADR-0043).
        await terms.DeleteAsync(request.Subject, request.Id, cancellationToken);
        await documentStore.DeleteAsync(document.StorageKey, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        MarkDocumentAsResumeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var all = await store.AllAsync(request.Subject, cancellationToken);
        var hit = all.FirstOrDefault(d => d.Id == request.Id);

        if (hit is null)
        {
            return false;
        }

        foreach (var document in all)
        {
            if (document.Id == request.Id)
            {
                document.Reclassify(DocumentKind.CurriculumVitae);
            }
            else if (document.Kind == DocumentKind.CurriculumVitae)
            {
                document.Reclassify(DocumentKind.Other);
            }
            else
            {
                continue;
            }

            await store.SaveAsync(document, cancellationToken);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        ChooseTemplateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resume = await resumes.GetAsync(request.Subject, cancellationToken);

        if (resume is null)
        {
            return false;
        }

        resume.ChooseTemplate(request.Template, clock.GetUtcNow());
        await resumes.SaveAsync(resume, cancellationToken);

        return true;
    }
}

/// <summary>Another person's documents — if they are released.</summary>
/// <remarks>
/// <strong>The company's view of the application folder.</strong> It hangs on
/// the ledger and on nothing else: no field on the document says who may see
/// it, and no application table is asked here (ADR-0020). A revocation takes
/// effect on the next call (ADR-0013).
/// </remarks>
public sealed record VisibleDocumentsQuery(SubjectId Subject, TenantId Company)
    : IQueryMessage<IReadOnlyList<DocumentRecord>>;

/// <summary>The content of a released document.</summary>
public sealed record VisibleDocumentContentQuery(SubjectId Subject, TenantId Company, Guid Id)
    : IQueryMessage<(DocumentRecord Document, byte[] Content)?>;

/// <summary>Answers the company's questions.</summary>
public sealed class DocumentQueries(
    IDocumentRecordStore store,
    IDocumentStore documentStore,
    IConsentGate gate) :
    IRequestHandler<VisibleDocumentsQuery, IReadOnlyList<DocumentRecord>>,
    IRequestHandler<VisibleDocumentContentQuery, (DocumentRecord Document, byte[] Content)?>
{
    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    public async Task<IReadOnlyList<DocumentRecord>> Handle(
        VisibleDocumentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // FIRST ASK, THEN READ. The other way round the data would already be in
        // memory when the ledger says no — and the next person to insert a line
        // here would have it in hand.
        var may = await gate.MaySeeDocumentsAsync(
            request.Subject, request.Company, cancellationToken);

        return may
            ? await store.AllAsync(request.Subject, cancellationToken)
            : [];
    }

    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    public async Task<(DocumentRecord Document, byte[] Content)?> Handle(
        VisibleDocumentContentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var may = await gate.MaySeeDocumentsAsync(
            request.Subject, request.Company, cancellationToken);

        if (!may)
        {
            return null;
        }

        var document = await store.GetAsync(request.Subject, request.Id, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var content = await documentStore.GetAsync(document.StorageKey, cancellationToken);

        return content is null ? null : (document, content);
    }
}
