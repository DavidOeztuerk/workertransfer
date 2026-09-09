using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using JobsEntwurf = WorkerTransfer.Jobs.Infrastructure.Entwurf;
using JobsPorts = WorkerTransfer.Jobs.Application.Ports;
using ProfilEntwurf = WorkerTransfer.Profile.Infrastructure.Entwurf;
using ProfilPorts = WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Ein Fehlschlag meldet die <strong>Art</strong>, nie den Inhalt (ADR-0024 §4).
/// </summary>
/// <remarks>
/// <para>ADR-0024 §4 sagt zu: „Fehler melden die Fehlerart, nie den Inhalt — ein
/// Test prüft, dass ein Netzwerkfehler den Prompt nicht mitschleppt."
/// <strong>Den Test gab es nicht.</strong> Der Quelltext war richtig; er hing
/// allein daran, dass niemand beim nächsten Fehlerzweig ein
/// <c>{fehler.Message}</c> anhängt — und genau das ist die naheliegendste
/// Bewegung, weil ein Fehlschlag der Moment ist, in dem man mehr sehen will.</para>
///
/// <para>Der Selbstbeschreibungstext einer Person gehört in dieselbe Klasse wie
/// ein Lebenslauf: <c>product-scope.md</c> verbietet ihn im Protokoll. Eine
/// Fehlermeldung ist ein Protokolleintrag, der zusätzlich noch zum Aufrufer
/// zurückgeht.</para>
///
/// <para>Beide Dienste geben dieselbe Zusage, deshalb steht sie an einer Stelle:
/// die Reihe wäre sonst zweimal dieselbe, und eine der beiden bliebe beim
/// nächsten Zweig zurück.</para>
/// </remarks>
public class EntwurfsfehlerTests
{
    /// <summary>Was in keiner Meldung auftauchen darf.</summary>
    private const string Geheim = "Ich bin 1998 aus Aleppo gekommen und pflege meine Mutter.";

    private const string Wunsch = "bitte kuerzer";

    /// <summary>
    /// Ein Verbindungsfehler nennt die Art — und trägt den Prompt nicht mit.
    /// </summary>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task Ein_Verbindungsfehler_traegt_den_Prompt_nicht_mit(string seite)
    {
        var fehler = await Fange(
            seite,
            new Stubhandler(_ => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("kein Netz"))));

        fehler.Message.Should().Contain("HttpRequestException", "die Art gehoert hinein");
        OhneInhalt(fehler);
    }

    /// <summary>
    /// Eine Fehlerantwort nennt den Statuscode — und nicht den fremden Rumpf.
    /// </summary>
    /// <remarks>
    /// Der Rumpf des Anbieters ist besonders verlockend, weil dort oft steht,
    /// was fehlte. Er kann aber den eingeschickten Text zitieren, und dann steht
    /// er über den Umweg des Anbieters doch im eigenen Protokoll.
    /// </remarks>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task Eine_Fehlerantwort_traegt_weder_Rumpf_noch_Prompt(string seite)
    {
        var fehler = await Fange(seite, new Stubhandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"invalid_request: '{Geheim}'")
            })));

        fehler.Message.Should().Contain("400", "der Statuscode ist die Art");
        OhneInhalt(fehler);
    }

    /// <summary>
    /// Ein Zeitablauf ist ein Anbieterfehler — und kommt nicht als 500 heraus.
    /// </summary>
    /// <remarks>
    /// <c>client.Timeout</c> wirft <c>TaskCanceledException</c> und
    /// <b>nicht</b> <c>HttpRequestException</c>. Ohne einen eigenen Zweig
    /// verliesse ein langsamer Anbieter den Dienst als unbehandelte Ausnahme,
    /// also als 500 — obwohl es dieselbe Aussage ist wie ein Verbindungsfehler.
    /// Gemessen als Prüfungsfund; dies ist der Test dazu.
    /// </remarks>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task Ein_Zeitablauf_wird_zum_Anbieterfehler(string seite)
    {
        var fehler = await Fange(
            seite,
            new Stubhandler(async marke =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), marke);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            zeitlimit: TimeSpan.FromMilliseconds(80));

        fehler.Message.Should().Contain("Zeitueberschreitung");
        OhneInhalt(fehler);
    }

    /// <summary>
    /// Legt der Aufrufer auf, ist nichts kaputt — und daraus wird keine 503.
    /// </summary>
    /// <remarks>
    /// Beide Fälle teilen sich <c>TaskCanceledException</c>. Ohne die Bedingung
    /// am <c>catch</c> würde ein abgebrochener Browser dem Anbieter angelastet,
    /// und die Zusage „503 heisst: der Anbieter antwortet nicht" wäre nur noch
    /// manchmal wahr.
    /// </remarks>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task Ein_Abbruch_des_Aufrufers_bleibt_ein_Abbruch(string seite)
    {
        using var abbruch = new CancellationTokenSource();

        var handler = new Stubhandler(async marke =>
        {
            await abbruch.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), marke);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var tat = async () => await Entwirf(seite, handler, TimeSpan.FromSeconds(30), abbruch.Token);

        await tat.Should().ThrowAsync<OperationCanceledException>(
            "wer auflegt, meldet keinen Anbieterausfall");
    }

    /// <summary>Keine Meldung trägt Inhalt — weder den Text noch den Wunsch.</summary>
    private static void OhneInhalt(Exception fehler)
    {
        fehler.Message.Should().NotContain("Aleppo", "kein Wort aus dem Text der Person");
        fehler.Message.Should().NotContain(Geheim);
        fehler.Message.Should().NotContain(Wunsch, "auch der Wunsch ist Inhalt");
    }

    private static async Task<Exception> Fange(
        string seite, Stubhandler handler, TimeSpan? zeitlimit = null)
    {
        try
        {
            await Entwirf(seite, handler, zeitlimit ?? TimeSpan.FromSeconds(30), default);
        }
        catch (Exception fehler)
        {
            return fehler;
        }

        throw new InvalidOperationException("es fiel keine Ausnahme — der Test prueft dann nichts");
    }

    private static Task<string> Entwirf(
        string seite, Stubhandler handler, TimeSpan zeitlimit, CancellationToken marke)
    {
        var fabrik = new Stubfabrik(handler);

        if (seite == "profil")
        {
            var einstellungen = new ProfilEntwurf.Entwurfseinstellungen
            {
                Schluessel = "probe",
                Zeitueberschreitung = zeitlimit
            };

            return new ProfilEntwurf.HttpEntwerfer(fabrik, Options.Create(einstellungen))
                .EntwirfAsync(
                    new ProfilPorts.Entwurfslage("Titel", Geheim, ["Python"], Wunsch), marke);
        }

        var jobs = new JobsEntwurf.Entwurfseinstellungen
        {
            Schluessel = "probe",
            Zeitueberschreitung = zeitlimit
        };

        return new JobsEntwurf.HttpEntwerfer(fabrik, Options.Create(jobs))
            .EntwirfAsync(
                new JobsPorts.Anzeigenentwurf("Titel", Geheim, ["Python"], "Berlin", Wunsch),
                marke);
    }

    /// <summary>Eine Fabrik, die immer denselben Stub ausliefert.</summary>
    private sealed class Stubfabrik(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Ein Handler, der tut, was ihm mitgegeben wurde.</summary>
    private sealed class Stubhandler(Func<CancellationToken, Task<HttpResponseMessage>> tat)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            tat(cancellationToken);
    }
}
