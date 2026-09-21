using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Ablage;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>Wie das Annehmen einer DocumentRecord ausgegangen ist.</summary>
public abstract record DocumentResult
{
    private DocumentResult()
    {
    }

    /// <summary>Angenommen.</summary>
    public sealed record Erledigt(DocumentRecord DocumentRecord) : DocumentResult;

    /// <summary>Mit der Datei stimmt etwas nicht.</summary>
    public sealed record Abgelehnt(string Grund) : DocumentResult;

    /// <summary>Es sind schon genug.</summary>
    public sealed record ZuViele : DocumentResult;
}

/// <summary>Eine DocumentRecord beilegen.</summary>
/// <param name="Inhalt">Die Bytes. Was sie sind, entscheidet ihre Signatur.</param>
public sealed record AddDocumentCommand(
    SubjectId Wer,
    string? Name,
    DocumentKind Art,
    ReadOnlyMemory<byte> Inhalt) : IBefehl<DocumentResult>;

/// <summary>Die eigenen Unterlagen.</summary>
public sealed record MyDocumentRecordsQuery(SubjectId Wer) : IAbfrage<IReadOnlyList<DocumentRecord>>;

/// <summary>Der Inhalt einer DocumentRecord, samt ihrer Zeile.</summary>
public sealed record DocumentContentQuery(SubjectId Wer, Guid Id)
    : IAbfrage<(DocumentRecord DocumentRecord, byte[] Inhalt)?>;

/// <summary>Eine DocumentRecord wieder wegnehmen.</summary>
public sealed record DeleteDocumentRecordCommand(SubjectId Wer, Guid Id) : IBefehl<bool>;

/// <summary>Diese Datei ist der CurriculumVitae — die vorige CurriculumVitae-Datei nicht mehr.</summary>
public sealed record MarkDocumentAsResumeCommand(SubjectId Wer, Guid Id) : IBefehl<bool>;

/// <summary>Die ResumeTemplate des eigenen Lebenslaufs wählen.</summary>
public sealed record VorlageWaehlenBefehl(SubjectId Wer, ResumeTemplate ResumeTemplate) : IBefehl<bool>;

/// <summary>Führt die Unterlagen.</summary>
public sealed class DocumentCommands(
    IDocumentRecordStore store,
    IResumeStore lebenslaeufe,
    ITermStore terms,
    IDocumentStore ablage,
    TimeProvider uhr) :
    IRequestHandler<AddDocumentCommand, DocumentResult>,
    IRequestHandler<MyDocumentRecordsQuery, IReadOnlyList<DocumentRecord>>,
    IRequestHandler<DocumentContentQuery, (DocumentRecord DocumentRecord, byte[] Inhalt)?>,
    IRequestHandler<DeleteDocumentRecordCommand, bool>,
    IRequestHandler<MarkDocumentAsResumeCommand, bool>,
    IRequestHandler<VorlageWaehlenBefehl, bool>
{
    /// <inheritdoc />
    public async Task<DocumentResult> Handle(
        AddDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // DER TYP KOMMT AUS DEN BYTES. Ein `Content-Type` und eine Endung sind
        // beide frei waehlbar; wer ihnen glaubt, nimmt eine ausfuehrbare Datei
        // entgegen, weil jemand sie `zeugnis.png` genannt hat.
        var type = Typerkennung.Erkenne(request.Inhalt.Span);

        if (type is null)
        {
            return new DocumentResult.Abgelehnt(
                "only PNG, JPEG and PDF are accepted");
        }

        var vorhandene = await store.AllAsync(request.Wer, cancellationToken);

        if (request.Art == DocumentKind.CurriculumVitae)
        {
            foreach (var alt in vorhandene.Where(u => u.Art == DocumentKind.CurriculumVitae))
            {
                await store.DeleteAsync(request.Wer, alt.Id, cancellationToken);
                await terms.DeleteAsync(request.Wer, alt.Id, cancellationToken);
                await ablage.DeleteAsync(alt.Ablageschluessel, cancellationToken);
            }

            vorhandene = await store.AllAsync(request.Wer, cancellationToken);
        }

        if (vorhandene.Count >= DocumentRecord.HoechsteAnzahl)
        {
            return new DocumentResult.ZuViele();
        }

        DocumentRecord document;

        try
        {
            document = DocumentRecord.Nimm_an(
                request.Wer, request.Name, request.Art, type,
                request.Inhalt.Length, uhr.GetUtcNow());
        }
        catch (DocumentError error)
        {
            return new DocumentResult.Abgelehnt(error.Message);
        }

        // ERST DIE BYTES, DANN DIE ZEILE. Andersherum zeigte die Liste einen
        // Eintrag, dessen Inhalt es nie gab — und ein Klick darauf liefe ins
        // Leere, ohne dass jemand sagen koennte, warum. Eine Datei ohne Zeile
        // ist dagegen nur Platz, den ein Aufraeumen findet.
        await ablage.PutAsync(document.Ablageschluessel, request.Inhalt, cancellationToken);
        await store.SaveAsync(document, cancellationToken);

        return new DocumentResult.Erledigt(document);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentRecord>> Handle(
        MyDocumentRecordsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.AllAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(DocumentRecord DocumentRecord, byte[] Inhalt)?> Handle(
        DocumentContentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = await store.GetAsync(request.Wer, request.Id, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var content = await ablage.GetAsync(document.Ablageschluessel, cancellationToken);

        // Zeile ohne Datei: von aussen dasselbe wie „gibt es nicht". Etwas
        // anderes zu antworten hiesse, ueber einen eigenen Fehler zu reden,
        // wo der Aufrufer nichts davon hat.
        return content is null ? null : (document, content);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        DeleteDocumentRecordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var document = await store.GetAsync(request.Wer, request.Id, cancellationToken);

        if (document is null)
        {
            return false;
        }

        // ERST DIE ZEILE, DANN DIE BYTES — genau andersherum als beim Anlegen.
        // Bricht es dazwischen ab, bleibt eine Datei ohne Zeile: unerreichbar
        // und aufraeumbar. Andersherum bliebe eine Zeile ohne Datei stehen und
        // sähe fuer die Person aus wie eine DocumentRecord, die es noch gibt.
        await store.DeleteAsync(request.Wer, request.Id, cancellationToken);

        // UND DER FUND DAZU. Was aus dieser Datei gelesen wurde, ist eine
        // Aussage ueber sie — ohne sie hat es keinen Gegenstand mehr. Bliebe er
        // stehen, boete die ProfilePage weiter Woerter aus einem Reference an,
        // das die Person eben weggenommen hat (ADR-0043).
        await terms.DeleteAsync(request.Wer, request.Id, cancellationToken);
        await ablage.DeleteAsync(document.Ablageschluessel, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        MarkDocumentAsResumeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alle = await store.AllAsync(request.Wer, cancellationToken);
        var hit = alle.FirstOrDefault(u => u.Id == request.Id);

        if (hit is null)
        {
            return false;
        }

        foreach (var document in alle)
        {
            if (document.Id == request.Id)
            {
                document.Ordne_zu(DocumentKind.CurriculumVitae);
            }
            else if (document.Art == DocumentKind.CurriculumVitae)
            {
                document.Ordne_zu(DocumentKind.Other);
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
        VorlageWaehlenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resume = await lebenslaeufe.GetAsync(request.Wer, cancellationToken);

        if (resume is null)
        {
            return false;
        }

        resume.Waehle_vorlage(request.ResumeTemplate, uhr.GetUtcNow());
        await lebenslaeufe.SaveAsync(resume, cancellationToken);

        return true;
    }
}

/// <summary>Die Unterlagen einer anderen Person — wenn sie freigegeben sind.</summary>
/// <remarks>
/// <strong>Der Blick des Unternehmens auf die Bewerbungsmappe.</strong> Er
/// hängt am Ledger und an nichts sonst: kein Feld an der DocumentRecord sagt, wer
/// sie sehen darf, und keine Bewerbungstabelle wird hier befragt (ADR-0020).
/// Ein Revocation wirkt auf den nächsten Aufruf (ADR-0013).
/// </remarks>
public sealed record VisibleDocumentsQuery(SubjectId Wer, TenantId Firma)
    : IAbfrage<IReadOnlyList<DocumentRecord>>;

/// <summary>Der Inhalt einer freigegebenen DocumentRecord.</summary>
public sealed record VisibleDocumentContentQuery(SubjectId Wer, TenantId Firma, Guid Id)
    : IAbfrage<(DocumentRecord DocumentRecord, byte[] Inhalt)?>;

/// <summary>Beantwortet die Fragen des Unternehmens.</summary>
public sealed class DocumentQueries(
    IDocumentRecordStore store,
    IDocumentStore ablage,
    IConsentGate tor) :
    IRequestHandler<VisibleDocumentsQuery, IReadOnlyList<DocumentRecord>>,
    IRequestHandler<VisibleDocumentContentQuery, (DocumentRecord DocumentRecord, byte[] Inhalt)?>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<IReadOnlyList<DocumentRecord>> Handle(
        VisibleDocumentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ERST FRAGEN, DANN LESEN. Andersherum stünden die Daten schon im
        // Speicher, wenn der Ledger nein sagt — und der nächste, der hier eine
        // Zeile einfügt, hätte sie in der Hand.
        var darf = await tor.MaySeeDocumentsAsync(
            request.Wer, request.Firma, cancellationToken);

        return darf
            ? await store.AllAsync(request.Wer, cancellationToken)
            : [];
    }

    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<(DocumentRecord DocumentRecord, byte[] Inhalt)?> Handle(
        VisibleDocumentContentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var darf = await tor.MaySeeDocumentsAsync(
            request.Wer, request.Firma, cancellationToken);

        if (!darf)
        {
            return null;
        }

        var document = await store.GetAsync(request.Wer, request.Id, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var content = await ablage.GetAsync(document.Ablageschluessel, cancellationToken);

        return content is null ? null : (document, content);
    }
}
