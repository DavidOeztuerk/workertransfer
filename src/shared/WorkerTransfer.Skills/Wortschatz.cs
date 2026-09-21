using System.Collections.Frozen;

namespace WorkerTransfer.Skills;

/// <summary>Which words mean the same thing — and nothing else (ADR-0023).</summary>
/// <remarks>
/// The boundary that carries everything: here things are <em>renamed</em>, not
/// inferred.
/// <code>
/// allowed:    „Postgres“ and „PostgreSQL“ are the same word
/// forbidden:  „React“ means you can do JavaScript too
/// </code>
/// The first is a statement about language. The second is a statement about a
/// <em>person</em> — it credits them with a skill they did not name, at a place
/// where they cannot object.
/// <para>
/// From that it follows: no level, no weight, no ranking, no kinship. Nothing
/// out of which a number could later be built.
/// </para>
/// <para>
/// And: it never rejects anything and never invents anything. What the
/// vocabulary does not know stays exactly as it was typed — a list of permitted
/// skills would be a claim about which work exists, and it would be wrong at
/// every new technology and at every trade outside IT.
/// </para>
/// <para>
/// <b>Shared between profile-service and jobs-service</b>, and only the table.
/// The comparison happens in the browser: the skills of a job are held against
/// those of a profile, and two tables drifting apart produce exactly the gap
/// this one exists against — <c>"Postgres"</c> in the profile and
/// <c>"PostgreSQL"</c> in the job would then not be a tick.
/// </para>
/// <para>
/// What is <em>not</em> shared: the <c>SkillList</c> itself. Every service
/// keeps that for itself, because how many skills a profile or a job carries is
/// a decision of the service that owns it — there is no shared domain model
/// here (ADR-0004).
/// </para>
/// </remarks>
public static class Vocabulary
{
    /// <summary>Canonical name → its spellings.</summary>
    /// <remarks>
    /// Maintained in the repository, by pull request: traceable and open to
    /// contradiction. A list that feeds itself from the data ("these words
    /// often occur together") would again be an evaluation about people.
    /// <para>
    /// Deliberately kept small. Every entry is the claim that two words mean
    /// <em>the same thing</em> — in doubt it does not belong in here. And the
    /// list is not confined to IT: the transfer market knows care work, trades
    /// and sales, and people mistype there just the same.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Schreibweisen { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // Languages and runtimes
            ["JavaScript"] = ["js", "java script", "ecmascript"],
            ["TypeScript"] = ["ts", "type script"],
            ["Python"] = ["python3", "python 3", "py"],
            ["C#"] = ["c sharp", "csharp", "c-sharp"],
            ["C++"] = ["cpp", "c plus plus"],
            ["Go"] = ["golang"],
            ["Node.js"] = ["node", "nodejs", "node js"],
            [".NET"] = ["dotnet", "dot net", "net core", ".net core"],

            // Databases. "postgresql" does NOT stand here: the canonical name
            // is recognised anyway, and carrying it additionally as a spelling
            // of its own would be a line that does nothing and confuses
            // everything.
            ["PostgreSQL"] = ["postgres", "psql", "postgre"],
            ["MySQL"] = ["my sql"],
            ["MongoDB"] = ["mongo", "mongo db"],
            ["Microsoft SQL Server"] = ["mssql", "sql server", "ms sql"],

            // Tools and operations
            ["Kubernetes"] = ["k8s", "kubernets"],
            ["Docker"] = ["docker engine"],
            ["CI/CD"] = ["cicd", "ci cd", "continuous integration"],
            ["Amazon Web Services"] = ["aws"],
            ["Microsoft Azure"] = ["azure"],
            ["Google Cloud"] = ["gcp", "google cloud platform"],

            // Frameworks
            ["React"] = ["react.js", "reactjs", "react js"],
            ["Vue.js"] = ["vue", "vuejs"],
            ["Angular"] = ["angular.js", "angularjs"],

            // Outside IT — the transfer market is not only for developers.
            ["Buchhaltung"] = ["buchführung", "rechnungswesen"],
            ["Kundenbetreuung"] = ["kundenservice", "kundendienst", "customer support"],
            ["Projektleitung"] = ["projektmanagement", "project management"],
            ["Altenpflege"] = ["seniorenpflege", "altenpflegerin", "altenpfleger"],
            ["Elektroinstallation"] = ["elektrik", "elektroinstallateur"],
            ["Lohnbuchhaltung"] = ["entgeltabrechnung", "gehaltsabrechnung", "payroll"],

            // Trades, industry, construction (ADR-0039). As above the rule is:
            // two words mean THE SAME THING — "MIG/MAG" does not imply
            // "Schweißen". Spellings without an umlaut stand alongside, the
            // comparison is character-faithful.
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

            // Logistics and transport. "Code 95" is the key number under which
            // the same qualification stands in the driving licence.
            ["Staplerschein"] = ["gabelstaplerschein", "flurförderschein", "flurförderzeugschein"],
            ["Gabelstapler"] = ["stapler", "frontstapler", "forklift"],
            ["Hubwagen"] = ["handhubwagen", "gabelhubwagen", "ameise"],
            ["ADR-Schein"] = ["adr schein", "gefahrgutschein", "gefahrgutführerschein"],
            ["Berufskraftfahrer-Qualifikation"] = ["bkrfqg", "code 95", "schlüsselzahl 95", "modul 95"],

            // Health and care. "Pflegefachkraft" is the statutory renaming of
            // 2020.
            ["Pflegefachkraft"] = [
                "pflegefachmann", "pflegefachfrau", "examinierte pflegekraft",
                "gesundheits- und krankenpfleger", "gesundheits- und krankenpflegerin"
            ],
            ["Erste Hilfe"] = ["ersthelfer", "erste-hilfe-kurs", "first aid"],

            // Hospitality, retail, administration.
            ["HACCP"] = ["haccp-konzept", "haccp konzept"],
            ["Warenwirtschaft"] = ["wawi", "warenwirtschaftssystem"]
        };

    /// <summary>The canonical names themselves.</summary>
    public static IReadOnlyList<string> Namen { get; } = [.. Schreibweisen.Keys.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Lookup table: spelling → canonical name, regardless of upper and lower
    /// case.
    /// </summary>
    /// <remarks>
    /// The canonical name points at itself so it does not fall through the net
    /// when somebody writes it directly.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> NachSchreibweise = Build();

    /// <summary>The known spelling — or unchanged, whatever came in.</summary>
    /// <param name="skill">What somebody typed.</param>
    public static string Kanonisch(string skill)
    {
        var geputzt = (skill ?? string.Empty).Trim();

        return NachSchreibweise.TryGetValue(geputzt, out var name) ? name : geputzt;
    }

    /// <summary>Like <see cref="Kanonisch"/>, for a list. Empties fall away.</summary>
    /// <remarks>
    /// <b>Not</b> deduplicated: that belongs in <see cref="SkillList"/>, and
    /// there AFTER the renaming. The other way round, "Postgres, PostgreSQL"
    /// would become the same entry twice.
    /// </remarks>
    /// <param name="skills">What somebody typed.</param>
    public static IReadOnlyList<string> KanonischAlle(IEnumerable<string> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);

        return [.. skills.Select(Kanonisch).Where(name => name.Length > 0)];
    }

    private static FrozenDictionary<string, string> Build()
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
