using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Nachrichten;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.Scout.Domain.Hit;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Application.Kandidaten;

/// <summary>Eine Seite Hit für ein Unternehmen.</summary>
/// <param name="Firma">Das Unternehmen des Aufrufers — aus dem TOKEN, nie aus der ResumeRequest.</param>
/// <param name="Filter">Wonach gesucht wird. Nur Genanntes.</param>
/// <param name="Seitenlaenge">Wie viele Zeilen die Seite höchstens trägt.</param>
/// <param name="Zeiger">Wo es weitergeht, als Text aus der ResumeRequest.</param>
/// <param name="Stelle">
/// Die Anzeige, gegen die das Erreichbarkeits-Häkchen gebildet wird, oder
/// <c>null</c>.
/// </param>
/// <remarks>
/// <para><strong>Die Job filtert nicht.</strong> Sie ändert an der Menge der
/// Hit nichts — sie fügt jedem Hit eine Auskunft hinzu. Ohne sie tragen
/// alle einen Strich, und das ist eine vollständige Antwort (ADR-0041).</para>
/// </remarks>
public sealed record KandidatenAbfrage(
    TenantId Firma,
    SearchFilter Filter,
    int Seitenlaenge,
    string? Cursor,
    Guid? Job = null) : IAbfrage<HitPage>;

/// <summary>
/// Sucht über Genanntes, prüft den Ledger je Zeile, holt die Belege dazu.
/// </summary>
/// <remarks>
/// <para><strong>Die Reihenfolge der vier Schritte ist die Zusage dieses
/// Dienstes</strong>, und jeder von ihnen steht in ADR-0036:</para>
/// <list type="number">
/// <item>profile-service sucht — <strong>ausschliesslich über genannte
/// Worte</strong> (Auflage 3, ADR-0033), und als ODER: wer eines der Worte
/// nennt, ist dabei. Unter UND wäre jedes Häkchen gesetzt und die Liste
/// nutzlos.</item>
/// <item>Der Ledger wird <strong>einmal für die ganze Seite</strong> gefragt,
/// synchron und ohne Zwischenspeicher (ADR-0030, ADR-0013). Eine abweichende
/// Antwortlänge ist ein Fehler, kein Anlass zu raten.</item>
/// <item>Was nicht freigegeben ist, fällt weg. <strong>Nicht nachgeladen</strong>,
/// bis die Seite voll ist (ADR-0020 §4), und <strong>keine Gesamtzahl</strong>
/// (ADR-0026).</item>
/// <item>Erst jetzt, für die verbliebenen Hit, werden Belege
/// <strong>dazugeholt</strong> — nie zum Finden benutzt.</item>
/// </list>
///
/// <para><strong>None Sortierung nach Passung</strong> (Auflage 1). Die
/// Reihenfolge ist die, in der profile-service liefert:
/// <c>updated_at DESC, id DESC</c> — stabil und sachfremd. Hier wird nichts
/// umgestellt, und es gibt auch nichts, wonach man umstellen könnte: aus einer
/// Häkchenliste lässt sich kein Rang bilden, ohne vorher eine Zahl zu erfinden.
/// Wer hier je ein <c>OrderBy</c> einbaut, das aus dem Hit selbst gerechnet
/// ist, hat ADR-0022 durch die Hintertür geöffnet.</para>
/// </remarks>
public sealed class KandidatenHandler(
    IProfileSearch search,
    IConsentGate tor,
    IEvidenceSource evidences,
    IJobs jobs) : IRequestHandler<KandidatenAbfrage, HitPage>
{
    /// <summary>Obergrenze je Seite.</summary>
    /// <remarks>
    /// Jede Zeile kostet eine Frage an den Ledger und einen Belegabruf; ohne
    /// Deckel baute ein Aufrufer mit einer einzigen Adresszeile eine beliebig
    /// teure Abfrage. Und die Sammelfrage des Ledgers trägt höchstens hundert
    /// Paare — zwei je Person, also fünfzig Personen.
    /// </remarks>
    public const int MaxLength = 50;

    /// <summary>Was eine Seite trägt, wenn niemand etwas anderes sagt.</summary>
    public const int Vorgabe = 20;

    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    /// <exception cref="ProfilsucheSchweigt">profile-service antwortet nicht.</exception>
    public async Task<HitPage> Handle(
        KandidatenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var length = Math.Clamp(
            request.Seitenlaenge <= 0 ? Vorgabe : request.Seitenlaenge, 1, MaxLength);

        // 1. Gesucht wird ueber GENANNTES. Der Filter traegt gar nichts anderes
        //    — das steht als Typ in `SearchFilter`.
        var page = await search.SearchAsync(
            request.Filter, length, request.Cursor, cancellationToken);

        if (page.Eintraege.Count == 0)
        {
            return new HitPage([], page.Weiter);
        }

        // 2. EINE Frage fuer die ganze Seite, synchron und ohne Zwischenspeicher.
        var urteile = await tor.MaySeeAllAsync(
            [.. page.Eintraege.Select(term => term.Wer)], request.Firma, cancellationToken);

        if (urteile.Count != page.Eintraege.Count)
        {
            // Eine Antwort, die nicht zu den Fragen passt, laesst sich nicht
            // zuordnen — und falsch zuzuordnen hiesse, das Profile der falschen
            // Person zu zeigen. Also weigern statt raten.
            throw new ConsentGateSilent(
                $"{urteile.Count} Antworten auf {page.Eintraege.Count} Fragen");
        }

        // 3. Die Seite darf kuerzer werden. Auffuellen wuerde ueber die Anzahl
        //    der Runden verraten, wie viele Profile NICHT freigegeben sind.
        var sichtbar = page.Eintraege
            .Where((_, job) => urteile[job])
            .ToArray();

        // 4a. Die Job, EINMAL fuer die ganze Seite — sie ist fuer alle
        //     Hit dieselbe, und einmal je Zeile zu fragen waere derselbe
        //     Fehler, den die Sammelfrage an den Ledger behoben hat.
        //
        //     Ein Fehlschlag wird GESCHLUCKT: eine Job, die gerade nicht
        //     abrufbar ist, macht aus jedem Haekchen einen Strich und aus
        //     keinem ein Kreuz. Die Search faellt deswegen nicht aus — sie
        //     verliert eine Auskunft, und das sagt der Strich.
        var stellenaussage = await Aussage(request.Job, cancellationToken);
        var beiDerStelle = stellenaussage is null
            ? null
            : PlaceLookup.Find(stellenaussage.Postleitzahl, stellenaussage.Ort);

        // 4b. Erst jetzt die Belege — und je Hit einzeln, weil jeder eine
        //    eigene Release hat. Nacheinander und nicht parallel: gegenueber
        //    einem fremden Dienst ist eine Seite mit fuenfzig gleichzeitigen
        //    Anfragen ein kleiner Angriff, und die Obergrenze oben haelt die
        //    Zahl klein genug.
        var hit = new List<Hit>(sichtbar.Length);

        foreach (var term in sichtbar)
        {
            var bogen = await evidences.GetAsync(term.Wer, cancellationToken);

            hit.Add(new Hit(
                term.Wer,
                term.Ueberschrift,
                term.Text,
                term.Ort,
                term.RemoteMoeglich,
                term.Genannt,
                Haken(request.Filter, term.Genannt),
                bogen.Belege,
                bogen.State,
                Availability.Bilde(
                    term.Pendelstufe,
                    stellenaussage?.Anwesenheit,
                    // Der Ort der Person wird JE TREFFER aufgeloest und nirgends
                    // abgelegt: es entsteht keine Koordinatenspalte an einem
                    // Menschen (ADR-0041 §4).
                    PlaceLookup.Find(term.Ort),
                    beiDerStelle)));
        }

        return new HitPage(hit, page.Weiter);
    }

    /// <summary>Die Aussage der Anzeige — oder nichts, wenn sie schweigt.</summary>
    /// <remarks>
    /// Der Fehlschlag wird hier geschluckt und nicht weitergereicht: ohne
    /// Anzeige gibt es keinen Grund, die ganze HitPage zu verlieren. Der
    /// Preis ist ein Strich statt eines Haekchens, und der Strich sagt genau
    /// das Richtige — „hierzu ist nichts gesagt".
    /// </remarks>
    private async Task<JobStatement?> Aussage(
        Guid? job, CancellationToken cancellationToken)
    {
        if (job is not { } welche)
        {
            return null;
        }

        try
        {
            return await jobs.GetAsync(welche, cancellationToken);
        }
        catch (JobLookupSilent)
        {
            return null;
        }
    }

    /// <summary>Die Häkchenliste: ein Ja oder Nein je gesuchtem Wort.</summary>
    /// <remarks>
    /// <para>Je <em>gesuchtem</em> Wort und nicht je genannter Fähigkeit: die
    /// Liste beantwortet die Frage des Unternehmens und fasst nicht den Menschen
    /// zusammen. Wer nichts gesucht hat, bekommt eine leere Liste — und
    /// ausdrücklich kein „0 von 0".</para>
    ///
    /// <para>Verglichen wird kanonisch und ohne Gross-/Kleinschreibung: beide
    /// Seiten sind durch denselben Vocabulary gelaufen (ADR-0023), und ein Haken,
    /// der an der Schreibweise scheitert, wäre eine falsche Aussage über einen
    /// Menschen.</para>
    ///
    /// <para>Die Reihenfolge ist die der Search. Die Haken nach „erfüllt zuerst"
    /// zu ordnen wäre eine Sortierung nach Passung im Kleinen — und der Anfang
    /// derselben im Grossen.</para>
    /// </remarks>
    private static IReadOnlyList<Haken> Haken(
        SearchFilter filter, IReadOnlyList<string> genannt) =>
    [
        .. filter.GenannteWorte.Select(word => new Haken(
            word,
            genannt.Contains(word, StringComparer.OrdinalIgnoreCase)))
    ];
}
