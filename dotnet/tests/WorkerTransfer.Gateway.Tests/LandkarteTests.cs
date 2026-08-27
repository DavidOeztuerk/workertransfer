using FluentAssertions;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>Wohin welche Adresse zeigt — gemessen, nicht gelesen.</summary>
/// <remarks>
/// Die Landkarte ist der ganze Dienst. Sie zu prüfen, indem man sie liest, ist
/// dasselbe, wie sie zu schreiben; deshalb läuft hier das echte Gateway mit der
/// ausgelieferten Datei vor elf Attrappen, und jede Zeile ist eine Frage an
/// eine echte Adresse.
/// </remarks>
[Collection(LandschaftsSammlung.Name)]
public class LandkarteTests(Landschaft landschaft)
{
    /// <summary>Die Regelfälle: ein Präfix, ein Dienst.</summary>
    [Theory]
    [InlineData("/auth/login", "identity")]
    [InlineData("/me", "identity")]
    [InlineData("/me/companies", "identity")]
    [InlineData("/companies", "identity")]
    [InlineData("/companies/7f000001-0000-0000-0000-000000000000/members", "identity")]
    [InlineData("/invitations/accept", "identity")]
    [InlineData("/account/erasure", "identity")]
    [InlineData("/consent/check", "consent")]
    [InlineData("/consent/check-batch", "consent")]
    [InlineData("/profiles/me", "profile")]
    [InlineData("/candidates", "profile")]
    [InlineData("/resumes/me", "resume")]
    [InlineData("/portfolios/me", "portfolio")]
    [InlineData("/jobs", "jobs")]
    [InlineData("/jobs/7f000001-0000-0000-0000-000000000000", "jobs")]
    [InlineData("/applications/me", "applications")]
    [InlineData("/market/me", "transfer")]
    [InlineData("/transfers/me", "transfer")]
    [InlineData("/github/me", "github")]
    [InlineData("/notifications", "notification")]
    [InlineData("/notifications/me", "notification")]
    public async Task Eine_Adresse_landet_bei_ihrem_Dienst(string pfad, string erwartet)
    {
        var (dienst, angekommen) = await landschaft.Frage(pfad);

        dienst.Should().Be(erwartet);
        angekommen.Should().Be(pfad, "der Pfad wird durchgereicht, nicht umgeschrieben");
    }

    /// <summary>
    /// Die Ausnahmen: Adressen, die sich erst im letzten Segment unterscheiden.
    /// </summary>
    /// <remarks>
    /// Das ist die Stelle, an der ein Gateway ohne Prioritäten falsch liegt.
    /// </remarks>
    [Theory]
    [InlineData("/jobs/7f000001-0000-0000-0000-000000000000/applications", "applications")]
    [InlineData("/companies/me/jobs", "jobs")]
    [InlineData("/companies/me/application-stats", "applications")]
    [InlineData("/companies/me/profile", "companies")]
    [InlineData("/companies/by-slug/muster-gmbh", "companies")]
    [InlineData("/companies/withdrawal", "jobs")]
    [InlineData("/companies/7f000001-0000-0000-0000-000000000000/profile", "companies")]
    [InlineData("/me/notification-preferences", "notification")]
    public async Task Die_genauere_Regel_gewinnt(string pfad, string erwartet)
    {
        var (dienst, _) = await landschaft.Frage(pfad);

        dienst.Should().Be(erwartet);
    }

    /// <summary>
    /// Derselbe Pfad, zwei Bedeutungen — der Kopf entscheidet.
    /// </summary>
    /// <remarks>
    /// Das ist die Regel, um die es geht: ohne sie liefert ein Direktlink auf
    /// <c>/jobs</c> rohes JSON statt der Seite, und zwar genau dann, wenn
    /// jemand ihn teilt oder F5 drückt.
    /// </remarks>
    [Theory]
    [InlineData("/jobs")]
    [InlineData("/applications")]
    [InlineData("/transfers")]
    [InlineData("/github")]
    [InlineData("/profiles/me")]
    [InlineData("/companies/me/profile")]
    public async Task Eine_Navigation_landet_immer_bei_der_Oberflaeche(string pfad)
    {
        var (navigiert, _) = await landschaft.Frage(pfad, ("Sec-Fetch-Dest", "document"));

        navigiert.Should().Be("web");
    }

    /// <summary>Ein Programm holt Daten — und bekommt den Dienst.</summary>
    /// <remarks>
    /// <c>fetch</c> schickt <c>empty</c>, curl schickt den Kopf gar nicht.
    /// Beide dürfen die Regel nicht auslösen, sonst bekäme die Oberfläche ihre
    /// eigenen Daten nicht.
    /// </remarks>
    [Theory]
    [InlineData("empty")]
    [InlineData("image")]
    [InlineData("script")]
    [InlineData(null)]
    public async Task Alles_andere_als_document_geht_an_den_Dienst(string? kopfwert)
    {
        var koepfe = kopfwert is null
            ? Array.Empty<(string, string)>()
            : [("Sec-Fetch-Dest", kopfwert)];

        var (dienst, _) = await landschaft.Frage("/jobs", koepfe);

        dienst.Should().Be("jobs");
    }

    /// <summary>Die Oberfläche bekommt, was kein Dienst beansprucht.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/overview")]
    [InlineData("/login")]
    [InlineData("/careers/muster-gmbh")]
    [InlineData("/assets/index-abc123.js")]
    public async Task Was_kein_Dienst_beansprucht_ist_die_Oberflaeche(string pfad)
    {
        var (dienst, _) = await landschaft.Frage(pfad, ("Sec-Fetch-Dest", "document"));

        dienst.Should().Be("web");
    }

    /// <summary>
    /// <c>/erasure</c> und <c>/internal/notify</c> haben keine Route.
    /// </summary>
    /// <remarks>
    /// Beide sind Dienst-zu-Dienst-Eingänge hinter einem gemeinsamen
    /// Geheimnis. Über den öffentlichen Ursprung erreichbar wären sie „lösche
    /// alles über diesen Menschen", bewacht von einem Kopf. Sie fallen auf die
    /// Oberfläche, und die kennt sie nicht — was hier heißt: sie erreichen
    /// keinen der zehn Dienste.
    /// </remarks>
    [Theory]
    [InlineData("/erasure")]
    [InlineData("/internal/notify")]
    public async Task Die_Diensteingaenge_sind_von_aussen_nicht_erreichbar(string pfad)
    {
        var (ohneKopf, _) = await landschaft.Frage(pfad);
        var (mitKopf, _) = await landschaft.Frage(pfad, ("Sec-Fetch-Dest", "document"));

        ohneKopf.Should().BeOneOf("<keine Route>", "web");
        mitKopf.Should().Be("web");
    }

    /// <summary>Die Gesundheitsproben beantwortet das Gateway selbst.</summary>
    /// <remarks>
    /// Sonst kippte ein einzelner kranker Dienst das Gateway aus dem
    /// Lastverteiler und nähme die anderen neun mit.
    /// </remarks>
    [Fact]
    public async Task Die_Gesundheitsprobe_wird_nicht_weitergereicht()
    {
        using var antwort = await landschaft.Browser.GetAsync("/health/live");

        antwort.EnsureSuccessStatusCode();
        (await antwort.Content.ReadAsStringAsync()).Should().Contain("live");
    }

    /// <summary>Jede Anfrage bekommt eine Kennung, bevor sie sich aufteilt.</summary>
    [Fact]
    public async Task Ohne_Kennung_wird_eine_vergeben()
    {
        using var antwort = await landschaft.Browser.GetAsync("/jobs");

        antwort.Headers.GetValues(Korrelation.Kopf).Single().Should().NotBeEmpty();
    }

    /// <summary>Eine mitgebrachte Kennung wird durchgereicht, nicht überschrieben.</summary>
    /// <remarks>
    /// Sie kommt dann von einem Aufrufer, der schon eine Kette führt.
    /// </remarks>
    [Fact]
    public async Task Eine_mitgebrachte_Kennung_bleibt()
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/jobs");
        anfrage.Headers.TryAddWithoutValidation(Korrelation.Kopf, "meine-kette");

        using var antwort = await landschaft.Browser.SendAsync(anfrage);

        antwort.Headers.GetValues(Korrelation.Kopf).Single().Should().Be("meine-kette");
    }
}
