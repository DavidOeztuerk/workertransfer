using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Nachrichten;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Suchen;
using WorkerTransfer.Scout.Domain.Treffer;

namespace WorkerTransfer.Scout.Application.Kandidaten;

/// <summary>Eine Seite Treffer für ein Unternehmen.</summary>
/// <param name="Firma">Das Unternehmen des Aufrufers — aus dem TOKEN, nie aus der Anfrage.</param>
/// <param name="Filter">Wonach gesucht wird. Nur Genanntes.</param>
/// <param name="Seitenlaenge">Wie viele Zeilen die Seite höchstens trägt.</param>
/// <param name="Zeiger">Wo es weitergeht, als Text aus der Anfrage.</param>
public sealed record KandidatenAbfrage(
    TenantId Firma,
    Suchfilter Filter,
    int Seitenlaenge,
    string? Zeiger) : IAbfrage<Trefferseite>;

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
/// <item>Erst jetzt, für die verbliebenen Treffer, werden Belege
/// <strong>dazugeholt</strong> — nie zum Finden benutzt.</item>
/// </list>
///
/// <para><strong>Keine Sortierung nach Passung</strong> (Auflage 1). Die
/// Reihenfolge ist die, in der profile-service liefert:
/// <c>updated_at DESC, id DESC</c> — stabil und sachfremd. Hier wird nichts
/// umgestellt, und es gibt auch nichts, wonach man umstellen könnte: aus einer
/// Häkchenliste lässt sich kein Rang bilden, ohne vorher eine Zahl zu erfinden.
/// Wer hier je ein <c>OrderBy</c> einbaut, das aus dem Treffer selbst gerechnet
/// ist, hat ADR-0022 durch die Hintertür geöffnet.</para>
/// </remarks>
public sealed class KandidatenHandler(
    IProfilsuche suche,
    IEinwilligungstor tor,
    IBelege belege) : IRequestHandler<KandidatenAbfrage, Trefferseite>
{
    /// <summary>Obergrenze je Seite.</summary>
    /// <remarks>
    /// Jede Zeile kostet eine Frage an den Ledger und einen Belegabruf; ohne
    /// Deckel baute ein Aufrufer mit einer einzigen Adresszeile eine beliebig
    /// teure Abfrage. Und die Sammelfrage des Ledgers trägt höchstens hundert
    /// Paare — zwei je Person, also fünfzig Personen.
    /// </remarks>
    public const int Hoechstlaenge = 50;

    /// <summary>Was eine Seite trägt, wenn niemand etwas anderes sagt.</summary>
    public const int Vorgabe = 20;

    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    /// <exception cref="ProfilsucheSchweigt">profile-service antwortet nicht.</exception>
    public async Task<Trefferseite> Handle(
        KandidatenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var laenge = Math.Clamp(
            request.Seitenlaenge <= 0 ? Vorgabe : request.Seitenlaenge, 1, Hoechstlaenge);

        // 1. Gesucht wird ueber GENANNTES. Der Filter traegt gar nichts anderes
        //    — das steht als Typ in `Suchfilter`.
        var seite = await suche.SucheAsync(
            request.Filter, laenge, request.Zeiger, cancellationToken);

        if (seite.Eintraege.Count == 0)
        {
            return new Trefferseite([], seite.Weiter);
        }

        // 2. EINE Frage fuer die ganze Seite, synchron und ohne Zwischenspeicher.
        var urteile = await tor.DarfSehenAlleAsync(
            [.. seite.Eintraege.Select(fund => fund.Wer)], request.Firma, cancellationToken);

        if (urteile.Count != seite.Eintraege.Count)
        {
            // Eine Antwort, die nicht zu den Fragen passt, laesst sich nicht
            // zuordnen — und falsch zuzuordnen hiesse, das Profil der falschen
            // Person zu zeigen. Also weigern statt raten.
            throw new EinwilligungSchweigt(
                $"{urteile.Count} Antworten auf {seite.Eintraege.Count} Fragen");
        }

        // 3. Die Seite darf kuerzer werden. Auffuellen wuerde ueber die Anzahl
        //    der Runden verraten, wie viele Profile NICHT freigegeben sind.
        var sichtbar = seite.Eintraege
            .Where((_, stelle) => urteile[stelle])
            .ToArray();

        // 4. Erst jetzt die Belege — und je Treffer einzeln, weil jeder eine
        //    eigene Freigabe hat. Nacheinander und nicht parallel: gegenueber
        //    einem fremden Dienst ist eine Seite mit fuenfzig gleichzeitigen
        //    Anfragen ein kleiner Angriff, und die Obergrenze oben haelt die
        //    Zahl klein genug.
        var treffer = new List<Treffer>(sichtbar.Length);

        foreach (var fund in sichtbar)
        {
            var bogen = await belege.HoleAsync(fund.Wer, cancellationToken);

            treffer.Add(new Treffer(
                fund.Wer,
                fund.Ueberschrift,
                fund.Text,
                fund.Ort,
                fund.RemoteMoeglich,
                fund.Genannt,
                Haken(request.Filter, fund.Genannt),
                bogen.Belege,
                bogen.Stand));
        }

        return new Trefferseite(treffer, seite.Weiter);
    }

    /// <summary>Die Häkchenliste: ein Ja oder Nein je gesuchtem Wort.</summary>
    /// <remarks>
    /// <para>Je <em>gesuchtem</em> Wort und nicht je genannter Fähigkeit: die
    /// Liste beantwortet die Frage des Unternehmens und fasst nicht den Menschen
    /// zusammen. Wer nichts gesucht hat, bekommt eine leere Liste — und
    /// ausdrücklich kein „0 von 0".</para>
    ///
    /// <para>Verglichen wird kanonisch und ohne Gross-/Kleinschreibung: beide
    /// Seiten sind durch denselben Wortschatz gelaufen (ADR-0023), und ein Haken,
    /// der an der Schreibweise scheitert, wäre eine falsche Aussage über einen
    /// Menschen.</para>
    ///
    /// <para>Die Reihenfolge ist die der Suche. Die Haken nach „erfüllt zuerst"
    /// zu ordnen wäre eine Sortierung nach Passung im Kleinen — und der Anfang
    /// derselben im Grossen.</para>
    /// </remarks>
    private static IReadOnlyList<Haken> Haken(
        Suchfilter filter, IReadOnlyList<string> genannt) =>
    [
        .. filter.GenannteWorte.Select(wort => new Haken(
            wort,
            genannt.Contains(wort, StringComparer.OrdinalIgnoreCase)))
    ];
}
