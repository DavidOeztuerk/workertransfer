using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Verbindungen;
using WorkerTransfer.GitHub.Infrastructure.Netz;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Ein GitHub aus Papier.</summary>
public sealed class Papierantwort(
    Func<HttpRequestMessage, (HttpStatusCode Code, string Rumpf)> antwort)
    : HttpMessageHandler
{
    /// <summary>Welche Pfade abgefragt wurden.</summary>
    public List<string> Pfade { get; } = [];

    /// <summary>Die Anfragen selbst — für Proben über Kopfzeilen.</summary>
    public List<HttpRequestMessage> Anfragen { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Pfade.Add(request.RequestUri!.PathAndQuery);
        Anfragen.Add(request);

        var (code, rumpf) = antwort(request);

        return Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(rumpf, Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>Eine Fabrik, die immer denselben Klienten liefert.</summary>
public sealed class Einzelfabrik(HttpMessageHandler behandler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(behandler, disposeHandler: false);
}

/// <summary>
/// Der echte Klient gegen ein GitHub aus Papier.
/// </summary>
/// <remarks>
/// Diese Reihe gibt es, weil eine Gegenprobe eine Lücke gezeigt hat: die
/// Reisetests ersetzen <see cref="IGitHub"/> durch eine Probe, und damit war
/// alles ungeprüft, was <em>im</em> Klienten steht — allen voran der
/// Fork-Filter. Forks wegzulassen ist keine Kosmetik: eine Kopie fremder Arbeit
/// ist kein Beleg für eigene, und sie unter „meine Repositories" zu zeigen wäre
/// genau die stillschweigende Behauptung, die ADR-0022 ausschließt.
/// </remarks>
public class HttpGitHubTests
{
    /// <summary>Antwortet mit dieser Liste — und auf jede Sprachfrage leer.</summary>
    /// <remarks>
    /// Ohne die Trennung bekäme eine <c>/languages</c>-Anfrage die
    /// Repository-<em>Liste</em> als Antwort. Das ist kein Wörterbuch, also
    /// wirft es — ein echter Fehler, der aber in der Probe läge und nicht im
    /// Code. Seit die Sprachen auch ohne Token geholt werden, trifft das jede
    /// Probe, die pauschal antwortet.
    /// </remarks>
    private static Papierantwort NurListe(string liste) =>
        new(anfrage =>
            anfrage.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, "{}")
                : (HttpStatusCode.OK, liste));

    private static HttpGitHub Baue(Papierantwort papier, string token = "") =>
        new(new Einzelfabrik(papier),
            Options.Create(new GitHubeinstellungen
            {
                Adresse = "https://api.github.test",
                Token = token
            }),
            NullLogger<HttpGitHub>.Instance);

    /// <summary>
    /// <strong>Jede Anfrage trägt einen <c>User-Agent</c> — sonst antwortet
    /// GitHub mit 403.</strong>
    /// </summary>
    /// <remarks>
    /// Kein Stilfehler, sondern der Grund, warum dieser Dienst nie funktioniert
    /// hat. GitHub verlangt den Kopf und weist ohne ihn ALLES ab — nicht mit
    /// 400, nicht mit einer Meldung, die das Wort nennt, sondern mit einem 403,
    /// das aussieht wie ein Ratenlimit. Gemessen am 04.09.2026 an derselben
    /// Adresse: mit Kopf 200, ohne Kopf 403.
    /// <para>
    /// Solange er fehlte, scheiterte auch der Gist-Nachweis — niemand konnte
    /// eine Verbindung je abschliessen, und die Oberfläche sagte dazu „github
    /// unavailable", was nach einem Ausfall bei GitHub aussieht.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Jede_Anfrage_traegt_eine_Benutzerkennung()
    {
        var papier = new Papierantwort(_ => (HttpStatusCode.OK, "[]"));
        var dienst = Baue(papier);

        await dienst.RepositoriesAsync("anna-dev");
        await dienst.HatNachweisgistAsync("anna-dev", "workertransfer-verify-abc");

        papier.Anfragen.Should().NotBeEmpty("sonst prüft der Test nichts");
        papier.Anfragen.Should().OnlyContain(
            anfrage => anfrage.Headers.UserAgent.Count > 0);
    }

    /// <summary>Die Topics kommen mit, weil sie eine NENNUNG sind.</summary>
    /// <remarks>
    /// Der stärkste Beleg von allen: „kubernetes" hat ein Mensch an das
    /// Repository geschrieben, kein Zähler abgeleitet. Und sie kosten nichts —
    /// GitHub liefert sie seit 2022 ohne Zutun in derselben Liste mit.
    /// </remarks>
    [Fact]
    public async Task Topics_kommen_mit()
    {
        var papier = NurListe("""
            [{"name":"leitwarte","description":"","language":"TypeScript",
              "stargazers_count":0,"html_url":"https://github.com/anna/leitwarte",
              "pushed_at":"2026-08-01T10:00:00Z","fork":false,
              "topics":["react","kubernetes","observability"]}]
            """);

        var eintrag = (await Baue(papier).RepositoriesAsync("anna-dev")).Repositories.Single();

        eintrag.Themen.Should().Equal("react", "kubernetes", "observability");
    }

    /// <summary>Ein Repository ohne Topics ist kein Fehler, sondern leer.</summary>
    [Fact]
    public async Task Ohne_Topics_bleibt_die_Liste_leer()
    {
        var papier = new Papierantwort(anfrage =>
            anfrage.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, "{}")
                : (HttpStatusCode.OK, """
                    [{"name":"still","description":"","language":null,
                      "stargazers_count":0,"html_url":"https://github.com/anna/still",
                      "pushed_at":null,"fork":false}]
                    """));

        var eintrag = (await Baue(papier).RepositoriesAsync("anna-dev")).Repositories.Single();

        eintrag.Themen.Should().BeEmpty();
        eintrag.Sprachen.Should().BeEmpty();
    }

    /// <summary>
    /// <strong>Auch ohne Token kommen die Sprachen — nur für weniger Repositories.</strong>
    /// </summary>
    /// <remarks>
    /// Vorher stand hier das Gegenteil, und es war ein gemessener Schaden:
    /// GitHub meldet für <c>workertransfer</c> sieben Sprachen, die Seite
    /// zeigte eine. Der Grund war das Ratenlimit — sechzig Anfragen in der
    /// Stunde —, aber sechzig reichen sehr wohl für zehn Repositories je Abruf.
    /// Der Verzicht sparte nichts und kostete die halbe Auskunft.
    /// </remarks>
    [Fact]
    public async Task Ohne_Token_kommen_die_Sprachen_trotzdem()
    {
        var papier = new Papierantwort(anfrage =>
            anfrage.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Python":70,"TypeScript":26,"CSS":1}""")
                : (HttpStatusCode.OK, """
                    [{"name":"werkzeug","description":"","language":"Python",
                      "stargazers_count":0,"html_url":"https://github.com/anna/werkzeug",
                      "pushed_at":null,"fork":false}]
                    """));

        var abzug = await Baue(papier).RepositoriesAsync("anna-dev");

        abzug.Repositories.Single().Sprachen
            .Should().BeEquivalentTo(["Python", "TypeScript", "CSS"]);

        // Und die Anteile bleiben draußen, mit oder ohne Token (ADR-0022 §2).
        System.Text.Json.JsonSerializer.Serialize(abzug.Repositories.Single())
            .Should().NotContain("70");

        abzug.SprachenVollstaendig.Should().BeTrue();
    }

    /// <summary>
    /// <strong>Reicht das Budget nicht, sagt der Abzug es — statt schmal auszusehen.</strong>
    /// </summary>
    /// <remarks>
    /// ADR-0022 §3: „Wer nichts auf GitHub hat, ist nicht schlechter, sondern
    /// woanders. Eine Ansicht, die das nicht sagt, lügt durch Auslassung."
    /// Dasselbe gilt eine Ebene tiefer — bei einem Repository, dessen Sprachen
    /// wir nicht holen konnten, läse sich die Hauptsprache wie die einzige.
    /// </remarks>
    [Fact]
    public async Task Mehr_Repositories_als_Budget_melden_sich_als_unvollstaendig()
    {
        var viele = string.Join(",", Enumerable.Range(0, 11).Select(nummer =>
            "{\"name\":\"repo" + nummer + "\",\"description\":\"\","
            + "\"language\":\"Go\",\"stargazers_count\":0,"
            + "\"html_url\":\"https://github.com/anna/repo" + nummer + "\","
            + "\"pushed_at\":null,\"fork\":false}"));

        var papier = new Papierantwort(anfrage =>
            anfrage.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Go":1}""")
                : (HttpStatusCode.OK, $"[{viele}]"));

        var abzug = await Baue(papier).RepositoriesAsync("anna-dev");

        abzug.Repositories.Should().HaveCount(11);
        abzug.SprachenVollstaendig.Should().BeFalse();

        // Die zehn zuletzt bearbeiteten haben ihre Sprachen — GitHub liefert
        // nach `pushed` sortiert, also trifft das Budget die ältesten.
        abzug.Repositories.Take(10).Should().OnlyContain(eintrag => eintrag.Sprachen.Count == 1);
        abzug.Repositories.Last().Sprachen.Should().BeEmpty();
    }

    /// <summary>
    /// <strong>Die Sprachen kommen als MENGE — die Bytes bleiben draussen.</strong>
    /// </summary>
    /// <remarks>
    /// GitHub antwortet mit <c>{"Go": 120000, "Shell": 210}</c>. Genau aus
    /// diesen Zahlen rechnete das gelöschte Paket sein „Können" als
    /// <c>bytes / total_bytes</c> — „eine eingecheckte Abhängigkeit schlägt
    /// jede sorgfältige Bibliothek. Wer wenig und gut schreibt, verliert"
    /// (ADR-0022 §2). Was nicht abgelegt wird, kann niemand aufsummieren.
    /// </remarks>
    [Fact]
    public async Task Sprachen_kommen_als_Menge_ohne_Bytes()
    {
        var papier = new Papierantwort(anfrage =>
            anfrage.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Go":120000,"Shell":210,"Dockerfile":90}""")
                : (HttpStatusCode.OK, """
                    [{"name":"werkzeug","description":"","language":"Go",
                      "stargazers_count":0,"html_url":"https://github.com/anna/werkzeug",
                      "pushed_at":null,"fork":false}]
                    """));

        var eintrag = (await Baue(papier, token: "ein-token")
            .RepositoriesAsync("anna-dev")).Repositories.Single();

        eintrag.Sprachen.Should().BeEquivalentTo(["Go", "Shell", "Dockerfile"]);

        // Die Zahlen dürfen nirgends mitgereist sein — auch nicht als Text.
        var abgelegt = System.Text.Json.JsonSerializer.Serialize(eintrag);
        abgelegt.Should().NotContain("120000");
        abgelegt.Should().NotContain("210");
    }

    /// <summary>Ohne Token wird gefragt, aber nicht beliebig oft.</summary>
    /// <remarks>
    /// Hier stand bis zum 05.09.2026 die Behauptung, ohne Token werde gar nicht
    /// gefragt — mit der Begründung, ein halb gefüllter Beleg sei schlechter
    /// als ein ehrlich leerer. Am echten Konto gemessen war der leere Beleg
    /// nicht ehrlich: er zeigte „Python", wo GitHub sieben Sprachen meldet,
    /// und sagte nirgends, dass er nur eine kennt. Gefragt wird jetzt für die
    /// zehn zuletzt bearbeiteten Repositories — elf Anfragen von sechzig —,
    /// und was darüber liegt, meldet der Abzug als unvollständig.
    /// </remarks>
    [Fact]
    public async Task Ohne_Token_bleibt_die_Zahl_der_Sprachabfragen_begrenzt()
    {
        var viele = string.Join(",", Enumerable.Range(0, 25).Select(nummer =>
            "{\"name\":\"repo" + nummer + "\",\"description\":\"\","
            + "\"language\":\"Go\",\"stargazers_count\":0,"
            + "\"html_url\":\"https://github.com/anna/repo" + nummer + "\","
            + "\"pushed_at\":null,\"fork\":false}"));

        var papier = new Papierantwort(anfrage =>
            anfrage.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Go":1}""")
                : (HttpStatusCode.OK, $"[{viele}]"));

        await Baue(papier).RepositoriesAsync("anna-dev");

        papier.Pfade
            .Count(pfad => pfad.Contains("/languages", StringComparison.Ordinal))
            .Should().Be(10);
    }

    /// <summary>Ein Fork ist kein Beleg für eigene Arbeit.</summary>
    [Fact]
    public async Task Forks_bleiben_draussen()
    {
        var papier = NurListe("""
            [
              {"name":"eigenes","description":"meins","language":"Go",
               "stargazers_count":3,"html_url":"https://github.com/anna/eigenes",
               "pushed_at":"2026-08-01T10:00:00Z","fork":false},
              {"name":"kopiert","description":"fremd","language":"Rust",
               "stargazers_count":9000,"html_url":"https://github.com/anna/kopiert",
               "pushed_at":"2026-08-20T10:00:00Z","fork":true}
            ]
            """);

        var gefunden = await Baue(papier).RepositoriesAsync("anna-dev");

        gefunden.Repositories.Select(eintrag => eintrag.Name).Should().Equal("eigenes");
    }

    /// <summary>Jedes Feld kommt von GitHub, keins ist gerechnet.</summary>
    [Fact]
    public async Task Die_Felder_werden_abgeschrieben()
    {
        var papier = NurListe("""
            [{"name":"werkzeug","description":"Ein Werkzeug","language":"Go",
              "stargazers_count":42,"html_url":"https://github.com/anna/werkzeug",
              "pushed_at":"2026-08-01T10:00:00Z","fork":false,
              "topics":["cli","verteilte-systeme"]}]
            """);

        var eintrag = (await Baue(papier).RepositoriesAsync("anna-dev")).Repositories.Single();

        // `BeEquivalentTo` und nicht `Be`: seit `Repository` zwei Listen trägt,
        // vergleicht die Gleichheit eines `record` sie über die REFERENZ — zwei
        // gleich befüllte Listen sind damit nie gleich. Der Vergleich muss
        // strukturell sein, sonst prüft er nur, ob es dasselbe Objekt ist.
        eintrag.Should().BeEquivalentTo(new Repository(
            "werkzeug", "Ein Werkzeug", "Go", 42,
            "https://github.com/anna/werkzeug",
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            // Ohne Token wird nicht je Repository nach den Sprachen gefragt —
            // sechzig Anfragen in der Stunde reichen dafür nicht.
            [],
            ["cli", "verteilte-systeme"]));
    }

    /// <summary>Fehlende Felder werden nicht erfunden.</summary>
    [Fact]
    public async Task Fehlendes_bleibt_leer_statt_geraten()
    {
        var papier = NurListe("""
            [{"name":"still","description":null,"language":null,
              "stargazers_count":0,"html_url":"https://github.com/anna/still",
              "pushed_at":null,"fork":false}]
            """);

        var eintrag = (await Baue(papier).RepositoriesAsync("anna-dev")).Repositories.Single();

        eintrag.Beschreibung.Should().BeEmpty();
        eintrag.Sprache.Should().BeNull();
        eintrag.ZuletztGeschoben.Should().BeNull();
    }

    /// <summary>Der Nachweis hängt an der Beschreibung, nicht am Inhalt.</summary>
    /// <remarks>
    /// Die Gist-Liste liefert die Beschreibung mit; ein Inhalt bräuchte einen
    /// Abruf je Gist. Bei sechzig Anfragen pro Stunde ohne Token ist das kein
    /// Detail.
    /// </remarks>
    [Fact]
    public async Task Der_Gist_wird_ueber_die_Beschreibung_gefunden()
    {
        var papier = NurListe("""
            [{"description":"irgendwas"},
             {"description":"workertransfer-verify-abc123"}]
            """);

        var github = Baue(papier);

        (await github.HatNachweisgistAsync("anna-dev", "abc123")).Should().BeTrue();
        (await github.HatNachweisgistAsync("anna-dev", "xyz789")).Should().BeFalse();
    }

    /// <summary>„Gibt es nicht" ist eine Antwort, kein Ausfall.</summary>
    [Fact]
    public async Task Ein_unbekanntes_Konto_ist_kein_Ausfall()
    {
        var papier = new Papierantwort(_ => (HttpStatusCode.NotFound, "{}"));
        var github = Baue(papier);

        (await github.RepositoriesAsync("gibt-es-nicht")).Repositories.Should().BeEmpty();
        (await github.HatNachweisgistAsync("gibt-es-nicht", "abc")).Should().BeFalse();
    }

    /// <summary>Ein Ratenlimit ist ein Systemzustand, kein Ergebnis.</summary>
    /// <remarks>
    /// Es auf „nicht bewiesen" abzubilden hieße, jemandem den Nachweis
    /// abzusprechen, weil wir gerade nicht fragen konnten.
    /// </remarks>
    [Fact]
    public async Task Ein_Ratenlimit_wird_zu_GitHubSchweigt()
    {
        var papier = new Papierantwort(_ => ((HttpStatusCode)403, "rate limited"));

        var versuch = async () => await Baue(papier).RepositoriesAsync("anna-dev");

        await versuch.Should().ThrowAsync<GitHubSchweigt>();
    }

    /// <summary>Ein unbrauchbarer Rumpf ebenso.</summary>
    [Fact]
    public async Task Ein_unbrauchbarer_Rumpf_wird_zu_GitHubSchweigt()
    {
        var papier = new Papierantwort(_ => (HttpStatusCode.OK, "kein json"));

        var versuch = async () => await Baue(papier).RepositoriesAsync("anna-dev");

        await versuch.Should().ThrowAsync<GitHubSchweigt>();
    }

    /// <summary>Gefragt wird nach eigenen Repositories, nach Datum sortiert.</summary>
    /// <remarks>
    /// <c>type=owner</c> lässt Mitgliedschaften draußen, <c>sort=pushed</c>
    /// macht die eine Seite zur ehrlichen Auswahl: die zuletzt geänderten.
    /// </remarks>
    [Fact]
    public async Task Gefragt_wird_nach_eigenen_und_zuletzt_geaenderten()
    {
        var papier = new Papierantwort(_ => (HttpStatusCode.OK, "[]"));

        await Baue(papier).RepositoriesAsync("anna-dev");

        papier.Pfade.Single().Should()
            .Be($"/users/anna-dev/repos?per_page={HttpGitHub.JeSeite}&sort=pushed&type=owner");
    }
}
