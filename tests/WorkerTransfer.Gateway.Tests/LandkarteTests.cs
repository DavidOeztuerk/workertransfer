using Girder.Abstractions.Observability;
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

    /// <summary>
    /// Auch die Bestandteile einer Seite finden die Oberfläche — sonst kommt
    /// die Seite an und bleibt leer.
    /// </summary>
    /// <remarks>
    /// <para><strong>Gemessen im Browser, nicht ausgedacht.</strong> Das Gateway
    /// lieferte das HTML von <c>/verify</c>, aber jedes <c>&lt;script src&gt;</c>
    /// darin lief ins Leere: vier 404 für <c>/@vite/client</c>,
    /// <c>/@react-refresh</c>, <c>/config.js</c> und <c>/src/main.tsx</c>. Die
    /// Navigationsstufe schrieb nur <em>Dokumente</em> auf das UI-Präfix um,
    /// und ein Skript schickt <c>Sec-Fetch-Dest: script</c>.</para>
    ///
    /// <para><strong>Warum es niemand bemerkt hat:</strong> Playwright fährt
    /// gegen <c>:5173</c>, also am Gateway vorbei. Über das Gateway hatte die
    /// Oberfläche nie geladen — nur fällt das erst auf, wenn jemand
    /// <c>:8090</c> im Browser öffnet. Und genau das ist der Weg, den das
    /// Helm-Chart als einzigen anbietet.</para>
    /// </remarks>
    [Theory]
    [InlineData("/@vite/client", "script")]
    [InlineData("/@react-refresh", "script")]
    [InlineData("/src/main.tsx", "script")]
    [InlineData("/config.js", "script")]
    [InlineData("/favicon.svg", "image")]
    [InlineData("/assets/index-abc123.css", "style")]
    public async Task Bestandteile_einer_Seite_finden_die_Oberflaeche(string pfad, string zweck)
    {
        var (dienst, _) = await landschaft.Frage(pfad, ("Sec-Fetch-Dest", zweck));

        dienst.Should().Be("web");
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
    /// Ein Dienst-zu-Dienst-Eingang erreicht von außen nie den Dienst, der ihn
    /// umsetzt.
    /// </summary>
    /// <remarks>
    /// Alle drei liegen hinter einem gemeinsamen Geheimnis. Über den
    /// öffentlichen Ursprung erreichbar wären sie „lösche alles über diesen
    /// Menschen", bewacht von einem Kopf.
    /// <para>
    /// Geprüft wird deshalb nicht „keine Route", sondern das, was wirklich
    /// zugesagt ist: <strong>der Besitzer sieht die Anfrage nicht.</strong> Der
    /// Unterschied ist bei <c>/companies/withdrawal</c> zu sehen — ohne eigene
    /// Route fällt der Pfad auf <c>/companies/{rest}</c> und landet bei
    /// identity-service, das ihn nicht kennt und mit 404 antwortet. Das ist
    /// genau richtig: kein Handler läuft, und die Antwort bestätigt nichts. Es
    /// „keine Route" zu nennen wäre eine Zusage, die die Landkarte nicht gibt.
    /// </para>
    /// </remarks>
    [Theory]
    // `/erasure` setzen acht Dienste um — keiner darf es sehen.
    [InlineData("/internal/notifications", "notification")]
    [InlineData("/erasure", "consent")]
    [InlineData("/erasure", "profile")]
    [InlineData("/erasure", "resume")]
    [InlineData("/erasure", "portfolio")]
    [InlineData("/erasure", "applications")]
    [InlineData("/erasure", "transfer")]
    [InlineData("/erasure", "github")]
    [InlineData("/erasure", "notification")]
    [InlineData("/internal/notify", "identity")]
    // Stand bis D2 mit Priorität 100 in der Landkarte und antwortete öffentlich
    // mit 401 — womit es bestätigte, dass es den Endpunkt gibt. Gebraucht wurde
    // die Route nie: identity-service ruft jobs-service direkt an.
    [InlineData("/companies/withdrawal", "jobs")]
    public async Task Ein_Diensteingang_erreicht_seinen_Besitzer_nicht(
        string pfad, string besitzer)
    {
        var (ohneKopf, _) = await landschaft.Frage(pfad);
        var (mitKopf, _) = await landschaft.Frage(pfad, ("Sec-Fetch-Dest", "document"));

        ohneKopf.Should().NotBe(besitzer);
        mitKopf.Should().Be("web", "eine Navigation gehört immer der Oberfläche");
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

        antwort.Headers.GetValues(CorrelationId.HeaderName).Single().Should().NotBeEmpty();
    }

    /// <summary>Eine mitgebrachte Kennung wird durchgereicht, nicht überschrieben.</summary>
    /// <remarks>
    /// Sie kommt dann von einem Aufrufer, der schon eine Kette führt.
    /// </remarks>
    [Fact]
    public async Task Eine_mitgebrachte_Kennung_bleibt()
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/jobs");
        anfrage.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "meine-kette");

        using var antwort = await landschaft.Browser.SendAsync(anfrage);

        antwort.Headers.GetValues(CorrelationId.HeaderName).Single().Should().Be("meine-kette");
    }
}
