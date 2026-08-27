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

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Pfade.Add(request.RequestUri!.PathAndQuery);

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
    private static HttpGitHub Baue(Papierantwort papier) =>
        new(new Einzelfabrik(papier),
            Options.Create(new GitHubeinstellungen { Adresse = "https://api.github.test" }),
            NullLogger<HttpGitHub>.Instance);

    /// <summary>Ein Fork ist kein Beleg für eigene Arbeit.</summary>
    [Fact]
    public async Task Forks_bleiben_draussen()
    {
        var papier = new Papierantwort(_ => (HttpStatusCode.OK, """
            [
              {"name":"eigenes","description":"meins","language":"Go",
               "stargazers_count":3,"html_url":"https://github.com/anna/eigenes",
               "pushed_at":"2026-08-01T10:00:00Z","fork":false},
              {"name":"kopiert","description":"fremd","language":"Rust",
               "stargazers_count":9000,"html_url":"https://github.com/anna/kopiert",
               "pushed_at":"2026-08-20T10:00:00Z","fork":true}
            ]
            """));

        var gefunden = await Baue(papier).RepositoriesAsync("anna-dev");

        gefunden.Select(eintrag => eintrag.Name).Should().Equal("eigenes");
    }

    /// <summary>Jedes Feld kommt von GitHub, keins ist gerechnet.</summary>
    [Fact]
    public async Task Die_Felder_werden_abgeschrieben()
    {
        var papier = new Papierantwort(_ => (HttpStatusCode.OK, """
            [{"name":"werkzeug","description":"Ein Werkzeug","language":"Go",
              "stargazers_count":42,"html_url":"https://github.com/anna/werkzeug",
              "pushed_at":"2026-08-01T10:00:00Z","fork":false}]
            """));

        var eintrag = (await Baue(papier).RepositoriesAsync("anna-dev")).Single();

        eintrag.Should().Be(new Repository(
            "werkzeug", "Ein Werkzeug", "Go", 42,
            "https://github.com/anna/werkzeug",
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>Fehlende Felder werden nicht erfunden.</summary>
    [Fact]
    public async Task Fehlendes_bleibt_leer_statt_geraten()
    {
        var papier = new Papierantwort(_ => (HttpStatusCode.OK, """
            [{"name":"still","description":null,"language":null,
              "stargazers_count":0,"html_url":"https://github.com/anna/still",
              "pushed_at":null,"fork":false}]
            """));

        var eintrag = (await Baue(papier).RepositoriesAsync("anna-dev")).Single();

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
        var papier = new Papierantwort(_ => (HttpStatusCode.OK, """
            [{"description":"irgendwas"},
             {"description":"workertransfer-verify-abc123"}]
            """));

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

        (await github.RepositoriesAsync("gibt-es-nicht")).Should().BeEmpty();
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
