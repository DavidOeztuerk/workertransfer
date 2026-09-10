using System.Collections.Frozen;

namespace WorkerTransfer.Skills;

/// <summary>Welche Wörter dasselbe meinen — und sonst nichts (ADR-0023).</summary>
/// <remarks>
/// Die Grenze, die alles trägt: hier wird <em>umbenannt</em>, nicht abgeleitet.
/// <code>
/// erlaubt:   „Postgres“ und „PostgreSQL“ sind dasselbe Wort
/// verboten:  „React“ heißt, du kannst auch JavaScript
/// </code>
/// Das erste ist eine Aussage über Sprache. Das zweite ist eine Aussage über
/// einen <em>Menschen</em> — es schreibt ihm eine Fähigkeit zu, die er nicht
/// genannt hat, an einer Stelle, an der er nicht widersprechen kann.
/// <para>
/// Daraus folgt: kein Niveau, kein Gewicht, keine Rangfolge, keine
/// Verwandtschaft. Nichts, woraus sich später eine Zahl bauen ließe.
/// </para>
/// <para>
/// Und: es lehnt nie etwas ab und erfindet nie etwas. Was der Wortschatz nicht
/// kennt, bleibt genau so stehen, wie es getippt wurde — eine Liste erlaubter
/// Fähigkeiten wäre eine Behauptung darüber, welche Arbeit es gibt, und läge
/// bei jeder neuen Technologie und bei jedem Beruf außerhalb der IT falsch.
/// </para>
/// <para>
/// <b>Geteilt zwischen profile-service und jobs-service</b>, und nur die
/// Tabelle. Der Abgleich findet im Browser statt: die Fähigkeiten einer Stelle
/// werden gegen die eines Profils gehalten, und zwei Tabellen, die
/// auseinanderlaufen, erzeugen genau die Lücke, gegen die diese hier existiert
/// — <c>„Postgres"</c> im Profil und <c>„PostgreSQL"</c> in der Stelle wären
/// dann kein Haken.
/// </para>
/// <para>
/// Was <em>nicht</em> geteilt ist: die <c>Faehigkeitenliste</c> selbst. Die
/// führt jeder Dienst für sich, denn wie viele Fähigkeiten ein Profil oder eine
/// Stelle trägt, ist eine Entscheidung des Dienstes, dem sie gehört — ein
/// geteiltes Domänenmodell gibt es hier nicht (ADR-0004).
/// </para>
/// </remarks>
public static class Wortschatz
{
    /// <summary>Kanonischer Name → seine Schreibweisen.</summary>
    /// <remarks>
    /// Im Repository gepflegt, per Pull Request: nachvollziehbar und
    /// widersprechbar. Eine Liste, die sich selbst aus den Daten füttert
    /// („diese Wörter kommen oft zusammen vor“), wäre wieder eine Auswertung
    /// über Menschen.
    /// <para>
    /// Bewusst klein gehalten. Jeder Eintrag ist die Behauptung, dass zwei
    /// Wörter <em>dasselbe</em> meinen — bei Zweifel gehört er nicht hinein.
    /// Und die Liste ist nicht auf IT beschränkt: der Transfermarkt kennt
    /// Pflege, Handwerk und Vertrieb, und dort verschreibt man sich genauso.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Schreibweisen { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // Sprachen und Laufzeiten
            ["JavaScript"] = ["js", "java script", "ecmascript"],
            ["TypeScript"] = ["ts", "type script"],
            ["Python"] = ["python3", "python 3", "py"],
            ["C#"] = ["c sharp", "csharp", "c-sharp"],
            ["C++"] = ["cpp", "c plus plus"],
            ["Go"] = ["golang"],
            ["Node.js"] = ["node", "nodejs", "node js"],
            [".NET"] = ["dotnet", "dot net", "net core", ".net core"],

            // Datenbanken. „postgresql“ steht hier NICHT: der kanonische Name
            // wird ohnehin erkannt, und ihn zusätzlich als eigene Schreibweise
            // zu führen wäre eine Zeile, die nichts tut und alles verwirrt.
            ["PostgreSQL"] = ["postgres", "psql", "postgre"],
            ["MySQL"] = ["my sql"],
            ["MongoDB"] = ["mongo", "mongo db"],
            ["Microsoft SQL Server"] = ["mssql", "sql server", "ms sql"],

            // Werkzeuge und Betrieb
            ["Kubernetes"] = ["k8s", "kubernets"],
            ["Docker"] = ["docker engine"],
            ["CI/CD"] = ["cicd", "ci cd", "continuous integration"],
            ["Amazon Web Services"] = ["aws"],
            ["Microsoft Azure"] = ["azure"],
            ["Google Cloud"] = ["gcp", "google cloud platform"],

            // Rahmenwerke
            ["React"] = ["react.js", "reactjs", "react js"],
            ["Vue.js"] = ["vue", "vuejs"],
            ["Angular"] = ["angular.js", "angularjs"],

            // Außerhalb der IT — der Transfermarkt ist nicht nur für Entwickler.
            ["Buchhaltung"] = ["buchführung", "rechnungswesen"],
            ["Kundenbetreuung"] = ["kundenservice", "kundendienst", "customer support"],
            ["Projektleitung"] = ["projektmanagement", "project management"],
            ["Altenpflege"] = ["seniorenpflege", "altenpflegerin", "altenpfleger"],
            ["Elektroinstallation"] = ["elektrik", "elektroinstallateur"],
            ["Lohnbuchhaltung"] = ["entgeltabrechnung", "gehaltsabrechnung", "payroll"],

            // Handwerk, Industrie, Bau (ADR-0039). Jede Zeile hier ist dieselbe
            // Behauptung wie jede Zeile oben: zwei Wörter meinen DASSELBE. Die
            // Verlockung ist hier größer, weil die Fachbegriffe eine sichtbare
            // Ordnung haben — jeder weiß, dass MIG/MAG ein Schweißverfahren
            // ist. Genau deshalb steht es hier: „MIG/MAG“ impliziert NICHT
            // „Schweißen“. Wer beides nennen will, nennt beides.
            //
            // Die Schreibweisen ohne Umlaut stehen ausdrücklich mit dabei: der
            // Vergleich ist zeichengetreu (nur Groß/Klein wird übersehen), und
            // „geruestbau“ fände sonst nichts.
            ["MIG/MAG"] = ["mig mag", "mig-mag", "migmag", "mig/mag-schweißen", "mig/mag-schweissen"],
            ["WIG"] = ["tig", "wolfram-inertgas", "wig-schweißen", "wig-schweissen"],
            ["Schweißerpass"] = ["schweisserpass", "schweißerpaß", "schweisserpaß"],
            ["Schweißfachmann"] = ["schweissfachmann", "schweißfachfrau", "schweissfachfrau"],
            ["CNC"] = ["c n c", "computerized numerical control", "computerised numerical control"],
            ["SPS"] = ["plc", "speicherprogrammierbare steuerung"],
            ["Zerspanungsmechanik"] = ["zerspanung", "zerspanungsmechaniker", "zerspanungsmechanikerin"],
            ["Kfz-Mechatronik"] = ["kfz-mechatroniker", "kfz mechatroniker", "kfz-mechaniker", "automechaniker"],
            ["Gerüstbau"] = ["geruestbau", "gerüstbauer", "geruestbauer", "gerüstmontage"],
            ["Trockenbau"] = ["trockenbauer", "trockenbaumontage"],

            // Logistik und Verkehr. „Code 95“ ist keine Ableitung, sondern der
            // Name derselben Sache: die Schlüsselzahl im Führerschein, mit der
            // die Qualifikation nach BKrFQG eingetragen wird.
            ["Staplerschein"] = ["gabelstaplerschein", "flurförderschein", "flurförderzeugschein"],
            ["Gabelstapler"] = ["stapler", "frontstapler", "forklift"],
            ["Hubwagen"] = ["handhubwagen", "gabelhubwagen", "ameise"],
            ["ADR-Schein"] = ["adr schein", "gefahrgutschein", "gefahrgutführerschein"],
            ["Berufskraftfahrer-Qualifikation"] = ["bkrfqg", "code 95", "schlüsselzahl 95", "modul 95"],

            // Gesundheit und Pflege. „Pflegefachkraft“ ist die gesetzliche
            // Umbenennung von 2020 — ein Umbenennen im buchstäblichsten Sinn.
            ["Pflegefachkraft"] = [
                "pflegefachmann", "pflegefachfrau", "examinierte pflegekraft",
                "gesundheits- und krankenpfleger", "gesundheits- und krankenpflegerin"
            ],
            ["Erste Hilfe"] = ["ersthelfer", "erste-hilfe-kurs", "first aid"],

            // Gastronomie, Handel, Verwaltung.
            ["HACCP"] = ["haccp-konzept", "haccp konzept"],
            ["Warenwirtschaft"] = ["wawi", "warenwirtschaftssystem"]
        };

    /// <summary>Die kanonischen Namen selbst.</summary>
    public static IReadOnlyList<string> Namen { get; } = [.. Schreibweisen.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Nachschlagetabelle: Schreibweise → kanonischer Name, ohne Rücksicht auf
    /// Groß-/Kleinschreibung.
    /// </summary>
    /// <remarks>
    /// Der kanonische Name zeigt auf sich selbst, damit er nicht durchs Raster
    /// fällt, wenn ihn jemand direkt schreibt.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> NachSchreibweise = Baue();

    /// <summary>Die bekannte Schreibweise — oder unverändert, was hereinkam.</summary>
    /// <param name="faehigkeit">Was jemand getippt hat.</param>
    public static string Kanonisch(string faehigkeit)
    {
        var geputzt = (faehigkeit ?? string.Empty).Trim();

        return NachSchreibweise.TryGetValue(geputzt, out var name) ? name : geputzt;
    }

    /// <summary>Wie <see cref="Kanonisch"/>, für eine Liste. Leeres fällt weg.</summary>
    /// <remarks>
    /// <b>Nicht</b> entdoppelt: das gehört in <see cref="Faehigkeitenliste"/>,
    /// und zwar NACH dem Umbenennen. Andersherum würde aus „Postgres,
    /// PostgreSQL“ zweimal derselbe Eintrag.
    /// </remarks>
    /// <param name="faehigkeiten">Was jemand getippt hat.</param>
    public static IReadOnlyList<string> KanonischAlle(IEnumerable<string> faehigkeiten)
    {
        ArgumentNullException.ThrowIfNull(faehigkeiten);

        return [.. faehigkeiten.Select(Kanonisch).Where(name => name.Length > 0)];
    }

    private static FrozenDictionary<string, string> Baue()
    {
        var tabelle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, schreibweisen) in Schreibweisen)
        {
            tabelle[name] = name;

            foreach (var schreibweise in schreibweisen)
            {
                tabelle[schreibweise] = name;
            }
        }

        return tabelle.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
