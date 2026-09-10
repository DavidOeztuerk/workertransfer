using System.Text.Json;
using FluentAssertions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// Die Routenkarte deckt jede Route ab — und zwar ohne laufenden Stapel.
/// </summary>
/// <remarks>
/// <c>docs/routenkarte.yml</c> haelt fuer jeden Endpunkt fest, was er in den
/// drei Handlungsformen antwortet. Gefahren wird sie von
/// <c>scripts/routenkarte.sh</c> gegen den laufenden Stapel — das ist die
/// Pruefung der <em>Antworten</em>.
/// <para>
/// Diese Reihe hier prueft etwas anderes und braucht dafuer keinen Stapel:
/// <strong>dass die Karte vollstaendig ist.</strong> Ohne sie waere sie genau
/// so lange richtig, bis jemand eine Route hinzufuegt — und der Tag, an dem das
/// passiert, ist der Tag, an dem niemand daran denkt. Eine Karte, die still
/// unvollstaendig wird, ist schlimmer als keine: sie sieht aus wie eine Zusage.
/// </para>
/// </remarks>
public sealed class RoutenkarteTests
{
    private sealed class Eintrag
    {
        public string Methode { get; init; } = "";
        public string Pfad { get; init; } = "";
        public int Ohne { get; init; }
        public int Person { get; init; }
        public int Firma { get; init; }

        /// <summary>
        /// Der Rumpf, den <c>scripts/routenkarte.sh</c> mitschickt.
        /// </summary>
        /// <remarks>
        /// Fast nirgends gesetzt: die meisten Endpunkte sehen gar nicht hinein,
        /// und der Durchlauf schickt sonst `{}`. Wo ein Endpunkt den Rumpf
        /// PRÜFT, mässe die Karte ohne dieses Feld die Antwort auf eine leere
        /// Anfrage und schriebe sie als die des Endpunkts auf — `PUT
        /// /account/language` etwa antwortete 422 statt 200, und die Karte
        /// hielte das für die Wahrheit.
        /// </remarks>
        public string? Rumpf { get; init; }
    }

    private sealed class Gruppe
    {
        public string Name { get; init; } = "";
        public string Warum { get; init; } = "";
        public List<Eintrag> Eintraege { get; init; } = [];
    }

    private static List<Gruppe> Karte()
    {
        var pfad = Path.Combine(Postgres_Ersatz.Repowurzel(), "docs", "routenkarte.yml");
        // Die Karte schreibt klein (`pfad`, `ohne`), die Klassen gross.
        var leser = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var wurzel = leser.Deserialize<Dictionary<string, List<Gruppe>>>(File.ReadAllText(pfad));

        return wurzel["gruppen"];
    }

    private static List<Eintrag> Eintraege() =>
        [.. Karte().SelectMany(g => g.Eintraege)];

    private static IReadOnlyList<string> Routen()
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "ocelot.json");
        var wurzel = JsonDocument.Parse(
            File.ReadAllText(pfad),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;

        return [.. wurzel.GetProperty("Routes").EnumerateArray()
            .Select(r => r.GetProperty("UpstreamPathTemplate").GetString()!)];
    }

    /// <summary>
    /// Passt ein Pfad zu einer Routenvorlage? <c>/auth/{rest}</c> deckt
    /// <c>/auth/login</c> ab, aber nicht <c>/auth</c> selbst.
    /// </summary>
    /// <remarks>
    /// Die Abfragezeichenkette wird abgeschnitten, bevor verglichen wird.
    /// Ocelot routet nach dem PFAD; ein Eintrag darf trotzdem
    /// <c>/jobs?page=2</c> nennen, weil die Karte festhält, was eine bestimmte
    /// ANFRAGE antwortet — und eine zweite Seite ist eine andere Anfrage als die
    /// erste.
    /// </remarks>
    private static bool Passt(string vorlage, string pfad)
    {
        var ohneAbfrage = pfad.Split('?')[0];

        var teileV = vorlage.Trim('/').Split('/');
        var teileP = ohneAbfrage.Trim('/').Split('/');

        // Der letzte Platzhalter ist gierig: `/auth/{rest}` faengt auch
        // `/auth/company/{id}`. Genau so verhaelt sich Ocelot.
        var gierig = teileV.Length > 0
                     && teileV[^1].StartsWith('{')
                     && teileV[^1].EndsWith('}');

        if (gierig ? teileP.Length < teileV.Length : teileP.Length != teileV.Length)
        {
            return false;
        }

        for (var i = 0; i < teileV.Length; i++)
        {
            if (teileV[i].StartsWith('{'))
            {
                continue;
            }

            if (!string.Equals(teileV[i], teileP[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// DER TEST, DER EINE NEUE ROUTE ROT MACHT.
    /// </summary>
    /// <remarks>
    /// Jede Zeile in <c>ocelot.json</c> muss von mindestens einem Eintrag der
    /// Karte beruehrt werden. Wer eine Route hinzufuegt und die Karte nicht
    /// ergaenzt, hat einen Endpunkt, von dem niemand aufgeschrieben hat, was er
    /// antworten soll — und der faellt dann erst jemandem auf, der ihn findet.
    /// </remarks>
    [Fact]
    public void Jede_Route_hat_mindestens_einen_Eintrag()
    {
        var eintraege = Eintraege();

        foreach (var route in Routen())
        {
            eintraege.Should().Contain(
                e => Passt(route, e.Pfad),
                $"die Route '{route}' steht in ocelot.json, aber in keiner Zeile "
                + "von docs/routenkarte.yml — es ist also nicht aufgeschrieben, "
                + "was sie antworten soll");
        }
    }

    /// <summary>
    /// Die Gegenrichtung: kein Eintrag beschreibt einen Pfad, den es nicht gibt.
    /// </summary>
    /// <remarks>
    /// Bis auf die drei Dienst-zu-Dienst-Tueren — die stehen mit Absicht drin,
    /// gerade WEIL sie keine Route haben, und ihre erwartete Antwort ist 404.
    /// Ohne diese Ausnahme koennte man die Zusage „von aussen nicht erreichbar"
    /// nicht aufschreiben.
    /// </remarks>
    [Fact]
    public void Kein_Eintrag_beschreibt_einen_Pfad_ohne_Route()
    {
        var routen = Routen();

        foreach (var eintrag in Eintraege())
        {
            var getroffen = routen.Any(r => Passt(r, eintrag.Pfad));

            if (getroffen)
            {
                continue;
            }

            eintrag.Ohne.Should().Be(
                404,
                $"'{eintrag.Pfad}' hat keine Route — dann ist 404 die einzige "
                + "Antwort, die dort stehen darf");
            eintrag.Person.Should().Be(404);
            eintrag.Firma.Should().Be(404);
        }
    }

    /// <summary>Jede Gruppe sagt, warum ihre Zeilen so aussehen.</summary>
    /// <remarks>
    /// Eine Zahl ohne Begruendung ist eine Messung, keine Zusage — und beim
    /// naechsten Umbau aendert sie jemand, weil sie im Weg steht.
    /// </remarks>
    [Fact]
    public void Jede_Gruppe_traegt_eine_Begruendung()
    {
        foreach (var gruppe in Karte())
        {
            gruppe.Name.Should().NotBeNullOrWhiteSpace();
            gruppe.Warum.Trim().Length.Should().BeGreaterThan(
                80, $"die Gruppe '{gruppe.Name}' sagt nicht, warum ihre Zeilen so aussehen");
            gruppe.Eintraege.Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// Ohne Token ist die Antwort nie 200 — ausser wo es aufgeschrieben ist.
    /// </summary>
    /// <remarks>
    /// Die schaerfste Zeile der Karte, und deshalb steht sie als eigener Test
    /// da. Vier Endpunkte antworten anonym mit 2xx, und jeder aus einem Grund,
    /// der in seiner Gruppe steht: <c>/auth/session</c> beantwortet die Frage
    /// „bin ich angemeldet?", <c>/auth/resend-verification</c> antwortet fuer
    /// bekannte und unbekannte Adressen gleich, die Karriereseite ist
    /// oeffentlich — und <c>GET /jobs</c> ist es seit derselben Entscheidung:
    /// die Karriereseite holt ihre Stellen ueber genau diesen Endpunkt, und
    /// solange er 401 gab, war sie nur halb oeffentlich.
    /// <para>
    /// Wer einen vierten hinzufuegt, muss diese Liste anfassen — und dabei
    /// merken, dass er gerade etwas oeffentlich macht.
    /// </para>
    /// </remarks>
    [Fact]
    public void Anonym_antwortet_nur_was_ausdruecklich_offen_ist()
    {
        string[] offen =
        [
            "/auth/session",
            "/auth/resend-verification",
            "/companies/by-slug/gibt-es-nicht",
            "/companies/00000000-0000-0000-0000-000000000001/profile",
            "/jobs"
        ];

        foreach (var eintrag in Eintraege().Where(e => e.Ohne is >= 200 and < 300))
        {
            // Ohne Abfragezeichenkette: `/jobs?page=2` ist derselbe ENDPUNKT
            // wie `/jobs`, und die Liste hier zählt Endpunkte. Ein Parameter
            // macht nichts öffentlich, was es nicht schon war.
            offen.Should().Contain(
                eintrag.Pfad.Split('?')[0],
                $"'{eintrag.Methode} {eintrag.Pfad}' antwortet ohne Token mit "
                + $"{eintrag.Ohne} — das ist ein oeffentlicher Endpunkt, und "
                + "dass er einer ist, gehoert ausdruecklich hingeschrieben");
        }
    }

    /// <summary>
    /// Die Karte beschreibt wirklich drei Faelle und nicht zweimal denselben.
    /// </summary>
    /// <remarks>
    /// Waeren `person` und `firma` ueberall gleich, waere die dritte Spalte
    /// Zierde und ADR-0017 in dieser Karte nicht geprueft. Gemessen sind es
    /// heute ueber zwanzig Zeilen, in denen sie auseinandergehen.
    /// </remarks>
    [Fact]
    public void Die_mittlere_Spalte_unterscheidet_sich_wirklich()
    {
        var eintraege = Eintraege();

        eintraege.Count(e => e.Person != e.Firma).Should().BeGreaterThan(
            15, "sonst beschreibt die Karte zweimal denselben Fall");
    }
}

/// <summary>Findet die Repowurzel, ohne von einer Testcontainer-Klasse zu erben.</summary>
internal static class Postgres_Ersatz
{
    public static string Repowurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null
               && !File.Exists(Path.Combine(verzeichnis.FullName, "WorkerTransfer.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName
               ?? throw new InvalidOperationException("Repowurzel nicht gefunden.");
    }
}
