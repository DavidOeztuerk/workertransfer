using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using JobsEntwurf = WorkerTransfer.Jobs.Infrastructure.Entwurf;
using JobsPorts = WorkerTransfer.Jobs.Application.Ports;
using ProfilEntwurf = WorkerTransfer.Profile.Infrastructure.Draft;
using ProfilPorts = WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// A failure reports the <strong>kind</strong>, never the content (ADR-0024 §4).
/// </summary>
/// <remarks>
/// <para>ADR-0024 §4 promises: "Errors report the failure kind, never the
/// content — a test checks that a network error does not drag the prompt along."
/// <strong>That test did not exist.</strong> The source was correct; it hung
/// solely on nobody appending a <c>{fehler.Message}</c> at the next error
/// branch — and that is exactly the most obvious movement, because a failure is
/// the moment when one wants to see more.</para>
///
/// <para>A person's self-description belongs in the same class as a resume:
/// <c>product-scope.md</c> forbids it in the log. An error message is a log
/// entry that on top of that goes back to the caller.</para>
///
/// <para>Both services give the same promise, which is why it stands in one
/// place: the suite would otherwise be the same one twice, and one of the two
/// would fall behind at the next branch.</para>
/// </remarks>
public class EntwurfsfehlerTests
{
    /// <summary>What must appear in no message.</summary>
    private const string Geheim = "Ich bin 1998 aus Aleppo gekommen und pflege meine Mutter.";

    private const string Wunsch = "bitte kuerzer";

    /// <summary>
    /// A connection error names the kind — and does not carry the prompt along.
    /// </summary>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task A_connection_error_does_not_carry_the_prompt_along(string page)
    {
        var error = await Fange(
            page,
            new Stubhandler(_ => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("kein Netz"))));

        error.Message.Should().Contain("HttpRequestException", "die Art gehoert hinein");
        OhneInhalt(error);
    }

    /// <summary>
    /// An error response names the status code — and not the foreign body.
    /// </summary>
    /// <remarks>
    /// The provider's body is particularly tempting, because what was missing
    /// often stands in it. But it can quote the text that was sent in, and then
    /// that text stands in our own log after all, by way of the provider.
    /// </remarks>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task An_error_response_carries_neither_body_nor_prompt(string page)
    {
        var error = await Fange(page, new Stubhandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"invalid_request: '{Geheim}'")
            })));

        error.Message.Should().Contain("400", "der Statuscode ist die Art");
        OhneInhalt(error);
    }

    /// <summary>
    /// A timeout is a provider error — and does not come out as a 500.
    /// </summary>
    /// <remarks>
    /// <c>client.Timeout</c> throws <c>TaskCanceledException</c> and
    /// <b>not</b> <c>HttpRequestException</c>. Without a branch of its own a
    /// slow provider would leave the service as an unhandled exception, that is,
    /// as a 500 — although it is the same statement as a connection error.
    /// Measured as an audit finding; this is the test for it.
    /// </remarks>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task A_timeout_becomes_a_provider_error(string page)
    {
        var error = await Fange(
            page,
            new Stubhandler(async marke =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), marke);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            zeitlimit: TimeSpan.FromMilliseconds(80));

        // Per page, because the two services name the kind in their own words:
        // profile-service speaks English since its translation, jobs-service
        // not yet. Either way it is the KIND, never the content.
        error.Message.Should().Contain(page == "profil" ? "timeout" : "Zeitueberschreitung");
        OhneInhalt(error);
    }

    /// <summary>
    /// If the caller hangs up, nothing is broken — and no 503 comes out of it.
    /// </summary>
    /// <remarks>
    /// Both cases share <c>TaskCanceledException</c>. Without the condition on
    /// the <c>catch</c> an aborted browser would be blamed on the provider, and
    /// the promise "503 means: the provider does not answer" would be true only
    /// some of the time.
    /// </remarks>
    [Theory]
    [InlineData("profil")]
    [InlineData("anzeige")]
    public async Task An_abort_by_the_caller_stays_an_abort(string page)
    {
        using var cancellationToken = new CancellationTokenSource();

        var handler = new Stubhandler(async marke =>
        {
            await cancellationToken.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), marke);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var tat = async () => await Entwirf(page, handler, TimeSpan.FromSeconds(30), cancellationToken.Token);

        await tat.Should().ThrowAsync<OperationCanceledException>(
            "wer auflegt, meldet keinen Anbieterausfall");
    }

    /// <summary>No message carries content — neither the text nor the wish.</summary>
    private static void OhneInhalt(Exception error)
    {
        error.Message.Should().NotContain("Aleppo", "kein Wort aus dem Text der Person");
        error.Message.Should().NotContain(Geheim);
        error.Message.Should().NotContain(Wunsch, "auch der Wunsch ist Inhalt");
    }

    private static async Task<Exception> Fange(
        string page, Stubhandler handler, TimeSpan? zeitlimit = null)
    {
        try
        {
            await Entwirf(page, handler, zeitlimit ?? TimeSpan.FromSeconds(30), default);
        }
        catch (Exception error)
        {
            return error;
        }

        throw new InvalidOperationException("es fiel keine Ausnahme — der Test prueft dann nichts");
    }

    private static Task<string> Entwirf(
        string page, Stubhandler handler, TimeSpan zeitlimit, CancellationToken marke)
    {
        var fabrik = new Stubfabrik(handler);

        if (page == "profil")
        {
            var einstellungen = new ProfilEntwurf.DraftSettings
            {
                Schluessel = "probe",
                Zeitueberschreitung = zeitlimit
            };

            return new ProfilEntwurf.HttpDrafter(fabrik, Options.Create(einstellungen))
                .DraftAsync(
                    new ProfilPorts.DraftSituation("Titel", Geheim, ["Python"], Wunsch), marke);
        }

        var jobs = new JobsEntwurf.DraftSettings
        {
            Schluessel = "probe",
            Zeitueberschreitung = zeitlimit
        };

        return new JobsEntwurf.HttpDrafter(fabrik, Options.Create(jobs))
            .DraftAsync(
                new JobsPorts.AdvertDraft("Titel", Geheim, ["Python"], "Berlin", Wunsch),
                marke);
    }

    /// <summary>A factory that always hands out the same stub.</summary>
    private sealed class Stubfabrik(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>A handler that does what it was given.</summary>
    private sealed class Stubhandler(Func<CancellationToken, Task<HttpResponseMessage>> tat)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            tat(cancellationToken);
    }
}
