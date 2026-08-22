using System.Collections.Frozen;

namespace WorkerTransfer.Profile.Domain.Faehigkeiten;

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
/// <b>Dienstintern, und das ist vorläufig.</b> jobs-service braucht dieselbe
/// Tabelle: der Abgleich im Browser hält die Fähigkeiten einer Stelle gegen die
/// eines Profils, und zwei Tabellen, die auseinanderlaufen, erzeugen genau die
/// Lücke, gegen die diese hier existiert. Sobald jobs-service steht, gehört sie
/// nach <c>WorkerTransfer.Contracts.Skills</c> — vorher wäre es ein geteiltes
/// Paket mit einem Konsumenten.
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
            ["Elektroinstallation"] = ["elektrik", "elektroinstallateur"]
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
