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
    [InlineData("/scout/candidates", "scout")]
    [InlineData("/scout/searches", "scout")]
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
    [InlineData("/assessments", "assessment")]
    [InlineData("/assessments/me", "assessment")]
    public async Task Eine_Adresse_landet_bei_ihrem_Dienst(string pfad, string erwartet)
    {
        var (dienst, angekommen) = await landschaft.Frage(pfad);

        dienst.Should().Be(erwartet);
        angekommen.Should().Be(pfad, "der Pfad wird durchgereicht, nicht umgeschrieben");
    }

    /// <summary>Der abgeloeste Pfad trifft keinen Dienst mehr.</summary>
    /// <remarks>
    /// <c>/candidates</c> war bis zum 11.09.2026 die Kandidatenliste und ist mit
    /// dem Umzug der Oberflaeche gefallen (ADR-0036). Diese Reihe ist die
    /// Gegenprobe zur Zeile in <c>docs/routenkarte.yml</c>: dort steht 404 in
    /// allen vier Spalten, und hier steht, dass wirklich keine Route mehr
    /// dahintersteht — nicht etwa eine, die nur gerade nichts antwortet.
    /// </remarks>
    [Theory]
    [InlineData("/candidates")]
    [InlineData("/candidates/7f000001-0000-0000-0000-000000000000")]
    public async Task Der_abgeloeste_Pfad_trifft_keinen_Dienst_mehr(string pfad)
    {
        var (dienst, _) = await landschaft.Frage(pfad);

        dienst.Should().Be("<keine Route>");
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
        var (dienst, _) = await landschaft.Frage(pfad);

        dienst.Should().NotBe(besitzer);
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
