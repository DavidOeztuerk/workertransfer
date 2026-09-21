using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Ablage;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Skills;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>Eine DocumentRecord samt dem, was zuletzt darin gelesen wurde.</summary>
/// <param name="Unterlage">Die Datei.</param>
/// <param name="Fund">
/// Der Fund — oder <c>null</c> für „noch nie gelesen". Die drei Zustände
/// müssen unterscheidbar bleiben: nie gelesen, gelesen ohne Text, gelesen ohne
/// bekanntes Wort. Zwei davon zusammenzulegen hiesse, jemandem stillschweigend
/// zu sagen, in seinem Reference stehe nichts (ADR-0022 §3).
/// </param>
public sealed record DocumentRecordState(DocumentRecord DocumentRecord, DocumentTerms? Fund);

/// <summary>Was in meinen Unterlagen steht — ohne etwas zu lesen.</summary>
public sealed record MyTermsQuery(SubjectId Wer) : IAbfrage<IReadOnlyList<DocumentRecordState>>;

/// <summary>Lies meine Unterlagen. Der Knopf.</summary>
/// <remarks>
/// <strong>Ein Befehl und keine Abfrage, weil er schreibt</strong> — der Fund
/// wird abgelegt, und eine Abfrage liefe an <c>TransaktionsBehavior</c> vorbei,
/// sodass die Zeile nie festgeschrieben würde (dieselbe Falle wie bei
/// <c>POST /me/oauth/start</c> in github-service).
/// </remarks>
public sealed record ReadDocumentsCommand(SubjectId Wer) : IBefehl<IReadOnlyList<DocumentRecordState>>;

/// <summary>Liest die eigenen Unterlagen — auf Auslösung, und nur dann.</summary>
/// <remarks>
/// <para><strong>NIE BEIM HOCHLADEN, und das ist die halbe Regel.</strong>
/// „Auf Auslösung" ist die andere Hälfte und für sich genommen zahnlos: ein
/// Lesevorgang, der beim Upload mitliefe, wäre formal auch „ausgelöst" — durch
/// das Hochladen. Gemeint ist etwas anderes, und ADR-0004 sagt es für GitHub
/// schon: <em>einmal auf Bitte hinsehen ist etwas anderes als dauerhaft
/// hinterhersehen.</em> Ein Mensch drückt einen Knopf, liest, was gefunden
/// wurde, und entscheidet. Deshalb steht der Aufruf hier und in
/// <c>AddDocumentCommand</c> ausdrücklich nicht — und deshalb misst
/// <c>ErkennungsreiseTests</c> nach dem Hochladen den Zähler des Erkenners,
/// statt es zu behaupten.</para>
///
/// <para><strong>Es liest jedes Mal alles neu.</strong> Nur die ungelesenen zu
/// nehmen wäre sparsamer und falsch: der Vocabulary wächst per Pull Request,
/// und ein Reference, das vor der Erweiterung gelesen wurde, trüge seinen Fund
/// für immer unvollständig. Der Knopf heisst „lies meine Unterlagen" und tut
/// genau das.</para>
///
/// <para><strong>Der Volltext bleibt in diesem Aufruf.</strong> Er wird nicht
/// abgelegt, nicht protokolliert und nicht zurückgegeben — hinaus gehen allein
/// die kanonischen Namen, die der Vocabulary wiedererkannt hat.</para>
/// </remarks>
public sealed class DocumentTermsList(
    IDocumentRecordStore documents,
    ITermStore terms,
    IDocumentStore ablage,
    ITexterkennung erkennung,
    TimeProvider uhr) :
    IRequestHandler<MyTermsQuery, IReadOnlyList<DocumentRecordState>>,
    IRequestHandler<ReadDocumentsCommand, IReadOnlyList<DocumentRecordState>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentRecordState>> Handle(
        MyTermsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await State(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentRecordState>> Handle(
        ReadDocumentsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alle = await documents.AllAsync(request.Wer, cancellationToken);
        var bisher = await Nachschlag(request.Wer, cancellationToken);
        var state = new List<DocumentRecordState>(alle.Count);

        foreach (var document in alle)
        {
            var content = await ablage.GetAsync(document.Ablageschluessel, cancellationToken);

            if (content is null)
            {
                // Zeile ohne Datei. Von aussen ist das dasselbe wie „gibt es
                // nicht" — und einen Fund dazu zu erfinden wäre eine Aussage
                // über ein Dokument, das niemand mehr hat.
                state.Add(new DocumentRecordState(
                    document, bisher.GetValueOrDefault(document.Id)));
                continue;
            }

            var erkanntes = erkennung.Lies(content, document.Inhaltstyp);

            var term = DocumentTerms.Halte_fest(
                request.Wer,
                document.Id,
                erkanntes.TextGefunden,
                // DER WORTSCHATZ BENENNT UM UND FOLGERT NIE. Gefunden wird,
                // was er kennt: „schweissfachmann" im Reference wird zum Namen
                // „Schweißfachmann" — eine Aussage über Sprache. Was er nicht
                // kennt, bleibt ungefunden, und das ist die Grenze und nicht
                // die Luecke (ADR-0023).
                TermFinder.Find(erkanntes.Text),
                uhr.GetUtcNow());

            await terms.SaveAsync(term, cancellationToken);
            state.Add(new DocumentRecordState(document, term));
        }

        // ANTWORT AUS DEM, WAS EBEN GELESEN WURDE — und ausdrücklich nicht aus
        // einem neuen Lesen der Tabelle. `TransaktionsBehavior` schreibt erst
        // fest, wenn dieser Behandler zurückgekehrt ist; eine Abfrage von hier
        // aus sähe den eigenen Fund noch nicht und antwortete „nichts
        // gefunden". Gemessen: die Reihe war rot, das Reference war es nicht.
        return state;
    }

    private async Task<IReadOnlyList<DocumentRecordState>> State(
        SubjectId subject, CancellationToken cancellationToken)
    {
        var alle = await documents.AllAsync(subject, cancellationToken);
        var nachUnterlage = await Nachschlag(subject, cancellationToken);

        return
        [
            .. alle.Select(document => new DocumentRecordState(
                document, nachUnterlage.GetValueOrDefault(document.Id)))
        ];
    }

    private async Task<Dictionary<Guid, DocumentTerms>> Nachschlag(
        SubjectId subject, CancellationToken cancellationToken)
    {
        var gelesen = await terms.AllAsync(subject, cancellationToken);

        return gelesen
            .GroupBy(term => term.DocumentRecord)
            .ToDictionary(group => group.Key, group => group.First());
    }
}
