using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkerTransfer.Nachweis;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Die Betriebsauskunft dieses Dienstes — sieben Adressen.</summary>
/// <remarks>
/// <para><strong>Nicht am Gateway.</strong> ADR-0040 hat entschieden, dass das
/// Gateway eine API-Tür ist und keine Oberfläche liefert; dieser Nachweis hebt
/// das nicht auf. Jeder Dienst antwortet unter seinem eigenen Hafen, und
/// <c>make nachweis</c> sammelt ein.</para>
///
/// <para><strong>Eine Seite je Abschnitt, mit eigener Adresse.</strong> Ein
/// Abschnitt, den man verlinken kann, landet im Ticket bei dem, der handeln
/// muss. Eine Seite mit sieben Abschnitten wird überflogen.</para>
///
/// <para><strong>Die Seite und <c>bericht.json</c> kommen aus EINER
/// Lesung.</strong> Zwei Abfragepfade zu einer Aussage laufen auseinander, und
/// beim ersten Mal merkt es niemand — dann zeigt die Seite einen Befund, den
/// die Daten nicht kennen, und welcher von beiden ins unterschriebene Dokument
/// geriet, sagt hinterher niemand mehr.</para>
///
/// <para><strong>Ohne Berechtigung 404 und nicht 403.</strong> Eine
/// Betriebsoberfläche, deren Existenz man erraten kann, ist selbst schon eine
/// Auskunft: sie sagt, dass es hier etwas zu holen gibt. Dieselbe Antwort für
/// ein falsches und für ein nicht gesetztes Geheimnis — niemand soll „falsch
/// geraten“ von „noch nicht verdrahtet“ unterscheiden können.</para>
/// </remarks>
public static class NachweisEndpoints
{
    /// <summary>Die Abschnitte, ihre Adresse und ihre Überschrift.</summary>
    /// <remarks>
    /// <c>Ledger</c> hat keine eigene Adresse und erscheint unter
    /// <c>einwilligung</c>: für den Menschen, der die Seite liest, sind „was der
    /// Ledger hält“ und „wie ein Widerruf wirkt“ eine Frage. Zwei Adressen
    /// dafür wären zwei Halbseiten.
    /// </remarks>
    private static readonly (string Pfad, string Titel, Bereich[] Bereiche)[] Abschnitte =
    [
        ("ki", "KI — Modelle, Anbieter, Naht, Aufzeichnung", [Bereich.KI]),
        ("einwilligung", "Einwilligung — Ledger, Widerruf, Sichtbarkeit",
            [Bereich.Einwilligung, Bereich.Ledger]),
        ("loeschung", "Löschung — Kaskade und Nachweis", [Bereich.Loeschung]),
        ("grenze", "Grenze — Ziele und Jurisdiktion", [Bereich.Grenze])
    ];

    /// <summary>Bindet die sieben Adressen ein.</summary>
    /// <param name="app">Die Anwendung.</param>
    /// <returns>Die Anwendung.</returns>
    public static IEndpointRouteBuilder MapNachweisEndpunkte(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/nachweis", async (HttpContext context, CancellationToken ct) =>
        {
            if (!await Eingelassen(context)) { return; }

            await Seite(context, Uebersicht(await Lese(context, ct)), ct);
        });

        // EINE Route fuer alles darunter, und der Schalter entscheidet. Ein
        // unbekannter Abschnitt faellt damit auf 404 — nicht auf die Uebersicht
        // unter falscher Adresse, denn die waere eine Seite, die zwei Dinge
        // heisst, und beim naechsten Endpunkt erinnert sich niemand daran.
        app.MapGet("/nachweis/{abschnitt}", async (
            string abschnitt, HttpContext context, CancellationToken ct) =>
        {
            if (!await Eingelassen(context)) { return; }

            if (abschnitt == "bericht.json")
            {
                await Daten(context, await Lese(context, ct), ct);
                return;
            }

            if (abschnitt == "pflichten")
            {
                await Seite(context, Pflichten(await Lese(context, ct)), ct);
                return;
            }

            var gewaehlt = Abschnitte.FirstOrDefault(
                eintrag => eintrag.Pfad == abschnitt);

            if (gewaehlt.Pfad is null)
            {
                await NichtGefunden(context);
                return;
            }

            await Seite(context, Abschnitt(await Lese(context, ct), gewaehlt), ct);
        });

        return app;
    }

    // ------------------------------------------------------------------- Tür

    /// <summary>Darf dieser Aufrufer den Nachweis sehen?</summary>
    /// <remarks>
    /// <c>FixedTimeEquals</c> und nicht <c>==</c>: ein Vergleich, der beim
    /// ersten falschen Zeichen aufhört, verrät über seine Dauer, wie viele
    /// Zeichen stimmten. Derselbe Vergleich wie an jeder anderen internen Tür
    /// dieses Baumes.
    /// </remarks>
    private static async Task<bool> Eingelassen(HttpContext context)
    {
        var geheimnis = context.RequestServices
            .GetRequiredService<IOptions<Nachweisgeheimnis>>().Value.Geheimnis;

        if (string.IsNullOrEmpty(geheimnis)
            || !context.Request.Headers.TryGetValue(
                Nachweisgeheimnis.Kopf, out var vorgelegt))
        {
            await NichtGefunden(context);
            return false;
        }

        var erlaubt = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(vorgelegt.ToString()),
            Encoding.UTF8.GetBytes(geheimnis));

        if (!erlaubt)
        {
            await NichtGefunden(context);
        }

        return erlaubt;
    }

    private static Task NichtGefunden(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status404NotFound, "Request failed", "Not Found");

    // ---------------------------------------------------------------- Lesung

    private static Task<Lesung> Lese(HttpContext context, CancellationToken ct) =>
        context.RequestServices.GetRequiredService<Nachweislauf>().LeseAsync(ct);

    // ----------------------------------------------------------------- Daten

    /// <summary>Dieselbe Lesung, als Daten.</summary>
    /// <remarks>
    /// Die Gestalt ist absichtlich flach und englisch benannt: sie wird von
    /// <c>make nachweis</c> eingesammelt und in die kanonische Form gebracht,
    /// über die die Signatur läuft.
    /// </remarks>
    private static Task Daten(HttpContext context, Lesung lesung, CancellationToken ct)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsJsonAsync(
            new BerichtV1(
                lesung.Dienst,
                lesung.Zeitpunkt,
                [.. lesung.Befunde.Select(befund => new BefundV1(
                    befund.Id,
                    befund.Bereich.ToString().ToLowerInvariant(),
                    befund.Stand.ToString().ToLowerInvariant(),
                    befund.Zusammenfassung,
                    befund.Abhilfe,
                    [.. befund.Bezuege.Select(bezug => new BezugV1(
                        Rechtsbezug.Wort(bezug.Regelwerk),
                        bezug.Artikel,
                        bezug.Pflicht,
                        bezug.Leser,
                        bezug.Gilt))]))]),
            Formen,
            ct);
    }

    private static readonly JsonSerializerOptions Formen = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    /// <summary>Ein Bericht, wie ihn <c>make nachweis</c> einsammelt.</summary>
    private sealed record BerichtV1(
        [property: JsonPropertyName("service")] string Service,
        [property: JsonPropertyName("read_at")] DateTimeOffset ReadAt,
        [property: JsonPropertyName("findings")] IReadOnlyList<BefundV1> Findings);

    /// <summary>Ein Befund als Daten.</summary>
    private sealed record BefundV1(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("area")] string Area,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("remedy")] string Remedy,
        [property: JsonPropertyName("references")] IReadOnlyList<BezugV1> References);

    /// <summary>Ein Zitat als Daten — samt dem, was offenbleibt.</summary>
    private sealed record BezugV1(
        [property: JsonPropertyName("regime")] string Regime,
        [property: JsonPropertyName("article")] string Article,
        [property: JsonPropertyName("duty")] string Duty,
        [property: JsonPropertyName("open_question")] string OpenQuestion,
        [property: JsonPropertyName("applies")] string Applies);

    // ------------------------------------------------------------------ Seiten

    private static async Task Seite(
        HttpContext context, string rumpf, CancellationToken ct)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(rumpf, ct);
    }

    private static string Uebersicht(Lesung lesung)
    {
        var zeilen = string.Join("\n", Abschnitte.Select(abschnitt =>
        {
            var befunde = abschnitt.Bereiche
                .SelectMany(lesung.Aus)
                .ToList();

            return $"""
                <tr>
                  <td><a href="/nachweis/{abschnitt.Pfad}">{H(abschnitt.Titel)}</a></td>
                  <td>{Staende(befunde)}</td>
                </tr>
                """;
        }));

        return Geruest(lesung, "Übersicht", $"""
            <p class="zeile">Diese Seite ist <strong>maschinell erzeugte
            technische Evidenz</strong>. Sie ist kein Zertifikat, und keine
            akkreditierte Stelle hat sie beurteilt. Was ein Programm zeigen kann,
            ist, dass etwas vorhanden ist, wann es entstanden ist und dass es
            unverändert ist — <em>Angemessenheit</em> kann es nicht zeigen.</p>

            <table>
              <thead><tr><th>Abschnitt</th><th>Befunde</th></tr></thead>
              <tbody>
            {zeilen}
              </tbody>
            </table>

            <p class="zeile"><a href="/nachweis/pflichten">Pflichten</a> —
            Beobachtung, Artikel und was offenbleibt.
            <a href="/nachweis/bericht.json">bericht.json</a> — dieselbe Lesung
            als Daten.</p>
            """);
    }

    private static string Abschnitt(
        Lesung lesung, (string Pfad, string Titel, Bereich[] Bereiche) abschnitt)
    {
        var befunde = abschnitt.Bereiche.SelectMany(lesung.Aus).ToList();

        if (befunde.Count == 0)
        {
            return Geruest(lesung, abschnitt.Titel,
                "<p class=\"zeile\">Zu diesem Abschnitt beantwortet dieser "
                + "Dienst keine Frage. Das ist keine Aussage über die "
                + "Plattform — andere Dienste antworten hier.</p>");
        }

        return Geruest(lesung, abschnitt.Titel, string.Join("\n", befunde.Select(Karte)));
    }

    private static string Karte(Befund befund) => $"""
        <section class="befund {Klasse(befund.Stand)}">
          <h2><code>{H(befund.Id)}</code> <span class="stand">{Wort(befund.Stand)}</span></h2>
          <p>{H(befund.Zusammenfassung)}</p>
          <p class="abhilfe"><strong>{(befund.Stand == Stand.Erfuellt
              ? "Was diesen Befund umstößt" : "Was zu tun wäre")}:</strong>
          {H(befund.Abhilfe)}</p>
          {Zitate(befund)}
        </section>
        """;

    /// <summary>Die Zitate eines Befundes — und was sie offenlassen.</summary>
    /// <remarks>
    /// <strong><c>Leser</c> steht in derselben Zeile wie das Zitat, nie als
    /// Fußnote.</strong> Eine Fußnote überlebt weder ein Bildschirmfoto noch das
    /// Einfügen in eine fremde Tabelle — und dann steht die Pflicht ohne das,
    /// was sie offenlässt.
    /// </remarks>
    private static string Zitate(Befund befund) =>
        befund.Bezuege.Count == 0
            ? ""
            : $"""
              <table class="bezuege">
                <thead><tr><th>Artikel</th><th>wonach er fragt</th>
                <th>was Sie noch entscheiden</th></tr></thead>
                <tbody>
              {string.Join("\n", befund.Bezuege.Select(bezug => $"""
                  <tr>
                    <td>{H(bezug.Fundstelle)}{Frist(bezug)}</td>
                    <td>{H(bezug.Pflicht)}</td>
                    <td>{H(bezug.Leser)}</td>
                  </tr>
                  """))}
                </tbody>
              </table>
              """;

    /// <summary>
    /// Die Pflichtenseite — vier Spalten, und die vierte macht sie ehrlich.
    /// </summary>
    /// <remarks>
    /// <strong>Keine Urteilsspalte.</strong> Ein Artikel ist nichts, was eine
    /// Prüfung bestehen kann. Was hier steht, ist: wonach er fragt, was in
    /// diesem Dienst dafür beobachtet wurde, und was ein Mensch danach noch
    /// entscheidet.
    /// </remarks>
    private static string Pflichten(Lesung lesung)
    {
        var nach = lesung.Befunde
            .SelectMany(befund => befund.Bezuege.Select(bezug => (bezug, befund)))
            .GroupBy(paar => paar.bezug.Fundstelle, StringComparer.Ordinal)
            .OrderBy(gruppe => Rang(gruppe.First().bezug))
            .ThenBy(gruppe => gruppe.Key, StringComparer.Ordinal)
            .ToList();

        if (nach.Count == 0)
        {
            return Geruest(lesung, "Pflichten",
                "<p class=\"zeile\">Dieser Dienst belegt keine Pflicht.</p>");
        }

        var zeilen = string.Join("\n", nach.Select(gruppe =>
        {
            var bezug = gruppe.First().bezug;

            var beobachtet = string.Join("<br>", gruppe.Select(paar =>
                $"<code>{H(paar.befund.Id)}</code>: {H(paar.befund.Zusammenfassung)}"));

            return $"""
                <tr>
                  <td>{H(bezug.Fundstelle)}{Frist(bezug)}</td>
                  <td>{H(bezug.Pflicht)}</td>
                  <td>{beobachtet}</td>
                  <td>{H(bezug.Leser)}</td>
                </tr>
                """;
        }));

        return Geruest(lesung, "Pflichten", $"""
            <p class="zeile">Vier Spalten, und <strong>keine
            Urteilsspalte</strong>: ein Artikel ist nichts, was eine Prüfung
            bestehen kann. Die dritte Spalte sagt, was beobachtet wurde; die
            vierte, was danach ein Mensch entscheidet.</p>

            <table>
              <thead><tr>
                <th>Artikel</th><th>wonach er fragt</th>
                <th>was hier dafür spricht</th><th>was Sie noch entscheiden</th>
              </tr></thead>
              <tbody>
            {zeilen}
              </tbody>
            </table>
            """);
    }

    // ---------------------------------------------------------------- Handwerk

    private static int Rang(Rechtsbezug bezug) => bezug.Regelwerk switch
    {
        Regelwerk.Dsgvo => 0,
        Regelwerk.KiVo => 1,
        _ => 2
    };

    private static string Frist(Rechtsbezug bezug) =>
        bezug.Gilt.Length == 0 ? "" : $"<br><span class=\"frist\">{H(bezug.Gilt)}</span>";

    private static string Staende(IReadOnlyList<Befund> befunde) =>
        befunde.Count == 0
            ? "<span class=\"leer\">keine</span>"
            : string.Join(" ", befunde
                .GroupBy(befund => befund.Stand)
                .OrderBy(gruppe => gruppe.Key)
                .Select(gruppe =>
                    $"<span class=\"marke {Klasse(gruppe.Key)}\">"
                    + $"{gruppe.Count()}&nbsp;{Wort(gruppe.Key)}</span>"));

    private static string Wort(Stand stand) => stand switch
    {
        Stand.Erfuellt => "beobachtet",
        Stand.Hinweis => "Hinweis",
        Stand.Fehlt => "nicht eingelöst",
        _ => "nicht anwendbar"
    };

    private static string Klasse(Stand stand) => stand switch
    {
        Stand.Erfuellt => "gut",
        Stand.Hinweis => "hinweis",
        Stand.Fehlt => "fehlt",
        _ => "grau"
    };

    /// <summary>Jeder Text, der aus einer Lesung kommt, geht hier durch.</summary>
    /// <remarks>
    /// Die Zusammenfassungen tragen Hostnamen und Feldnamen, und beide dürfen
    /// spitze Klammern enthalten. Sie ungeprüft in eine Seite zu schreiben wäre
    /// dieselbe Sorte Lücke, die der Baum überall sonst dadurch schließt, dass
    /// React entkommt.
    /// </remarks>
    private static string H(string text) => Entkommen.Encode(text);

    /// <summary>Der Entkommer — Markup ja, Umlaute nein.</summary>
    /// <remarks>
    /// <c>HtmlEncoder.Default</c> entkommt <em>jedes</em> Zeichen außerhalb von
    /// ASCII, und eine deutsche Seite besteht dann zur Hälfte aus
    /// <c>&amp;#xF6;</c>. Das ist nicht falsch, aber es ist unlesbar — im
    /// Quelltext der Seite, in einem Unterschied und in jedem Bildschirmfoto,
    /// das jemand in ein Ticket klebt. Die Seite ist UTF-8; was entkommen
    /// werden MUSS, sind die Markup-Zeichen, und die entkommt auch dieser hier.
    /// </remarks>
    private static readonly HtmlEncoder Entkommen =
        HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All);

    /// <summary>Das Gerüst jeder Seite.</summary>
    /// <remarks>
    /// <c>$$"""</c> und nicht <c>$"""</c>: in einer rohen Zeichenkette bestimmt
    /// die Zahl der Dollarzeichen, wie viele geschweifte Klammern eine
    /// Einsetzung öffnen. Mit zweien bleibt jede einzelne Klammer im CSS eine
    /// Klammer, und nichts muss verdoppelt werden — die Alternative wäre ein
    /// Stilblock, in dem jede Klammer zweimal dasteht und beim nächsten Umbau
    /// jemand eine vergisst.
    /// </remarks>
    private static string Geruest(Lesung lesung, string titel, string rumpf) => $$"""
        <!doctype html>
        <html lang="de">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta name="robots" content="noindex, nofollow">
          <title>Nachweis — {{H(lesung.Dienst)}} — {{H(titel)}}</title>
          <style>
            :root { color-scheme: light dark; }
            body { font: 16px/1.5 system-ui, sans-serif; margin: 0 auto;
                   max-width: 60rem; padding: 1.5rem 1rem 4rem; }
            h1 { font-size: 1.4rem; margin: 0 0 .25rem; }
            h2 { font-size: 1.05rem; margin: 0 0 .5rem; }
            nav { margin: 1rem 0 2rem; font-size: .9rem; }
            nav a { margin-right: 1rem; }
            table { border-collapse: collapse; width: 100%; margin: 1rem 0; }
            th, td { border: 1px solid #8884; padding: .5rem; text-align: left;
                     vertical-align: top; font-size: .9rem; }
            th { font-weight: 600; }
            .befund { border: 1px solid #8884; border-left-width: 4px;
                      padding: 1rem; margin: 0 0 1rem; }
            .befund.gut { border-left-color: #2e7d32; }
            .befund.hinweis { border-left-color: #b26a00; }
            .befund.fehlt { border-left-color: #c62828; }
            .befund.grau { border-left-color: #888; }
            .stand { font-weight: 400; font-size: .85rem; opacity: .8; }
            .marke { font-size: .8rem; padding: .1rem .4rem; border: 1px solid #8884; }
            .abhilfe { font-size: .9rem; opacity: .9; }
            .frist { font-size: .8rem; opacity: .8; }
            .zeile { font-size: .9rem; }
            .fuss { margin-top: 3rem; font-size: .8rem; opacity: .8; }
            code { font-size: .85em; }
          </style>
        </head>
        <body>
          <h1>Nachweis — {{H(lesung.Dienst)}}</h1>
          <p class="zeile">{{H(titel)}} · gelesen am {{H(Stempel(lesung))}} UTC</p>
          <nav>
            <a href="/nachweis">Übersicht</a>
            <a href="/nachweis/ki">KI</a>
            <a href="/nachweis/einwilligung">Einwilligung</a>
            <a href="/nachweis/loeschung">Löschung</a>
            <a href="/nachweis/grenze">Grenze</a>
            <a href="/nachweis/pflichten">Pflichten</a>
            <a href="/nachweis/bericht.json">bericht.json</a>
          </nav>
        {{rumpf}}
          <p class="fuss">Maschinell erzeugte technische Evidenz über den Stand
          dieses Dienstes zum genannten Zeitpunkt. Sie beschreibt, was beobachtet
          wurde — nicht, ob es genügt. Diese Seite nennt keinen Menschen und
          keinen Wert; wo gezählt wird, wird über Menschen gezählt und nie über
          eine Person berichtet.</p>
        </body>
        </html>
        """;

    /// <summary>Der Zeitpunkt der Lesung, ohne die Kultur der Maschine.</summary>
    private static string Stempel(Lesung lesung) =>
        lesung.Zeitpunkt.UtcDateTime.ToString(
            "dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
