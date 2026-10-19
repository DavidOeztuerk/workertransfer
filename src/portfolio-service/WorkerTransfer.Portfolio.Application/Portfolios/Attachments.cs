using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Messages;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Domain.Documents;
using WorkerTransfer.Portfolio.Domain.Portfolios;

namespace WorkerTransfer.Portfolio.Application.Portfolios;

/// <summary>Stores a work sample.</summary>
/// <param name="FileName">What the file was called at the sender — a hint only.</param>
public sealed record StoreAttachmentCommand(
    SubjectId Subject, string FileName, string MediaType, Stream Content)
    : ICommandMessage<string>;

/// <inheritdoc cref="StoreAttachmentCommand" />
/// <remarks>
/// The name the file lies under is assigned by the store and returned. The
/// client sends it along afterwards when saving the portfolio — so it never
/// names where anything is written.
/// </remarks>
public sealed class StoreAttachmentHandler(IDocumentStore documents)
    : IRequestHandler<StoreAttachmentCommand, string>
{
    /// <inheritdoc />
    public Task<string> Handle(StoreAttachmentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return documents.PutAsync(
            request.Subject, request.FileName, request.MediaType, request.Content,
            cancellationToken);
    }
}

/// <summary>Fetches a work sample.</summary>
public sealed record AttachmentQuery(SubjectId Subject, string Name, SubjectId Caller)
    : IQueryMessage<StoredFile?>;

/// <summary>Asks the same gate as the portfolio itself.</summary>
/// <remarks>
/// <b>The same consent, no second gate</b> (ADR-0021). And on top of it the
/// question whether an entry names the attachment at all: otherwise a work
/// sample removed from the portfolio would stay reachable through its name.
/// <para>
/// One's own is read without asking the ledger — otherwise one could no longer
/// look at one's own work sample after taking the release back.
/// </para>
/// </remarks>
public sealed class AttachmentHandler(
    IPortfolioStore store, IDocumentStore documents, IConsentGate gate)
    : IRequestHandler<AttachmentQuery, StoredFile?>
{
    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    public async Task<StoredFile?> Handle(
        AttachmentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var isSelf = request.Subject == request.Caller;

        if (!isSelf && !await gate.MaySeeAsync(request.Subject, cancellationToken))
        {
            return null;
        }

        var portfolio = await store.GetAsync(request.Subject, cancellationToken);

        return portfolio?.Names(request.Name) == true
            ? await documents.GetAsync(request.Subject, request.Name, cancellationToken)
            : null;
    }
}
