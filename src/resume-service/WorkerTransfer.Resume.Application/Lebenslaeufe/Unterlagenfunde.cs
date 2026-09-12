using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Ablage;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Skills;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>Eine Unterlage samt dem, was zuletzt darin gelesen wurde.</summary>
/// <param name="Unterlage">Die Datei.</param>
/// <param name="Fund">
/// Der Fund — oder <c>null</c> für „noch nie gelesen". Die drei Zustände
/// müssen unterscheidbar bleiben: nie gelesen, gelesen ohne Text, gelesen ohne
/// bekanntes Wort. Zwei davon zusammenzulegen hiesse, jemandem stillschweigend
/// zu sagen, in seinem Zeugnis stehe nichts (ADR-0022 §3).
/// </param>
public sealed record Unterlagenstand(Unterlage Unterlage, Unterlagenfund? Fund);

/// <summary>Was in meinen Unterlagen steht — ohne etwas zu lesen.</summary>
public sealed record MeineFundeAbfrage(SubjectId Wer) : IAbfrage<IReadOnlyList<Unterlagenstand>>;

/// <summary>Lies meine Unterlagen. Der Knopf.</summary>
/// <remarks>
/// <strong>Ein Befehl und keine Abfrage, weil er schreibt</strong> — der Fund
/// wird abgelegt, und eine Abfrage liefe an <c>TransaktionsBehavior</c> vorbei,
/// sodass die Zeile nie festgeschrieben würde (dieselbe Falle wie bei
/// <c>POST /me/oauth/start</c> in github-service).
/// </remarks>
public sealed record UnterlagenLesenBefehl(SubjectId Wer) : IBefehl<IReadOnlyList<Unterlagenstand>>;

/// <summary>Liest die eigenen Unterlagen — auf Auslösung, und nur dann.</summary>
/// <remarks>
/// <para><strong>NIE BEIM HOCHLADEN, und das ist die halbe Regel.</strong>
/// „Auf Auslösung" ist die andere Hälfte und für sich genommen zahnlos: ein
/// Lesevorgang, der beim Upload mitliefe, wäre formal auch „ausgelöst" — durch
/// das Hochladen. Gemeint ist etwas anderes, und ADR-0004 sagt es für GitHub
/// schon: <em>einmal auf Bitte hinsehen ist etwas anderes als dauerhaft
/// hinterhersehen.</em> Ein Mensch drückt einen Knopf, liest, was gefunden
/// wurde, und entscheidet. Deshalb steht der Aufruf hier und in
/// <c>UnterlageHinzufuegenBefehl</c> ausdrücklich nicht — und deshalb misst
/// <c>ErkennungsreiseTests</c> nach dem Hochladen den Zähler des Erkenners,
/// statt es zu behaupten.</para>
///
/// <para><strong>Es liest jedes Mal alles neu.</strong> Nur die ungelesenen zu
/// nehmen wäre sparsamer und falsch: der Wortschatz wächst per Pull Request,
/// und ein Zeugnis, das vor der Erweiterung gelesen wurde, trüge seinen Fund
/// für immer unvollständig. Der Knopf heisst „lies meine Unterlagen" und tut
/// genau das.</para>
///
/// <para><strong>Der Volltext bleibt in diesem Aufruf.</strong> Er wird nicht
/// abgelegt, nicht protokolliert und nicht zurückgegeben — hinaus gehen allein
/// die kanonischen Namen, die der Wortschatz wiedererkannt hat.</para>
/// </remarks>
public sealed class Unterlagenfunde(
    IUnterlagenSpeicher unterlagen,
    IFundSpeicher funde,
    IAblage ablage,
    ITexterkennung erkennung,
    TimeProvider uhr) :
    IRequestHandler<MeineFundeAbfrage, IReadOnlyList<Unterlagenstand>>,
    IRequestHandler<UnterlagenLesenBefehl, IReadOnlyList<Unterlagenstand>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Unterlagenstand>> Handle(
        MeineFundeAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await Stand(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Unterlagenstand>> Handle(
        UnterlagenLesenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alle = await unterlagen.AlleAsync(request.Wer, cancellationToken);
        var bisher = await Nachschlag(request.Wer, cancellationToken);
        var stand = new List<Unterlagenstand>(alle.Count);

        foreach (var unterlage in alle)
        {
            var inhalt = await ablage.HoleAsync(unterlage.Ablageschluessel, cancellationToken);

            if (inhalt is null)
            {
                // Zeile ohne Datei. Von aussen ist das dasselbe wie „gibt es
                // nicht" — und einen Fund dazu zu erfinden wäre eine Aussage
                // über ein Dokument, das niemand mehr hat.
                stand.Add(new Unterlagenstand(
                    unterlage, bisher.GetValueOrDefault(unterlage.Id)));
                continue;
            }

            var erkanntes = erkennung.Lies(inhalt, unterlage.Inhaltstyp);

            var fund = Unterlagenfund.Halte_fest(
                request.Wer,
                unterlage.Id,
                erkanntes.TextGefunden,
                // DER WORTSCHATZ BENENNT UM UND FOLGERT NIE. Gefunden wird,
                // was er kennt: „schweissfachmann" im Zeugnis wird zum Namen
                // „Schweißfachmann" — eine Aussage über Sprache. Was er nicht
                // kennt, bleibt ungefunden, und das ist die Grenze und nicht
                // die Luecke (ADR-0023).
                Wortfund.Finde(erkanntes.Text),
                uhr.GetUtcNow());

            await funde.SichereAsync(fund, cancellationToken);
            stand.Add(new Unterlagenstand(unterlage, fund));
        }

        // ANTWORT AUS DEM, WAS EBEN GELESEN WURDE — und ausdrücklich nicht aus
        // einem neuen Lesen der Tabelle. `TransaktionsBehavior` schreibt erst
        // fest, wenn dieser Behandler zurückgekehrt ist; eine Abfrage von hier
        // aus sähe den eigenen Fund noch nicht und antwortete „nichts
        // gefunden". Gemessen: die Reihe war rot, das Zeugnis war es nicht.
        return stand;
    }

    private async Task<IReadOnlyList<Unterlagenstand>> Stand(
        SubjectId wer, CancellationToken cancellationToken)
    {
        var alle = await unterlagen.AlleAsync(wer, cancellationToken);
        var nachUnterlage = await Nachschlag(wer, cancellationToken);

        return
        [
            .. alle.Select(unterlage => new Unterlagenstand(
                unterlage, nachUnterlage.GetValueOrDefault(unterlage.Id)))
        ];
    }

    private async Task<Dictionary<Guid, Unterlagenfund>> Nachschlag(
        SubjectId wer, CancellationToken cancellationToken)
    {
        var gelesen = await funde.AlleAsync(wer, cancellationToken);

        return gelesen
            .GroupBy(fund => fund.Unterlage)
            .ToDictionary(gruppe => gruppe.Key, gruppe => gruppe.First());
    }
}
