using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using JobsDraft = WorkerTransfer.Jobs.Infrastructure.Draft;
using JobsPorts = WorkerTransfer.Jobs.Application.Ports;
using ProfileDraft = WorkerTransfer.Profile.Infrastructure.Draft;
using ProfilePorts = WorkerTransfer.Profile.Application.Ports;
using ScoutDraft = WorkerTransfer.Scout.Infrastructure.Draft;
using ScoutPorts = WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// A failure reports the <strong>kind</strong>, never the content (ADR-0024 §4).
/// </summary>
/// <remarks>
/// <para>ADR-0024 §4 promises: "Errors report the failure kind, never the
/// content — a test checks that a network error does not drag the prompt along."
/// <strong>That test did not exist.</strong> The source was correct; it hung
/// solely on nobody appending an <c>{error.Message}</c> at the next error
/// branch — and that is exactly the most obvious movement, because a failure is
/// the moment when one wants to see more.</para>
///
/// <para>A person's self-description belongs in the same class as a resume:
/// <c>product-scope.md</c> forbids it in the log. An error message is a log
/// entry that on top of that goes back to the caller.</para>
///
/// <para>All three services give the same promise, which is why it stands in
/// one place: the suite would otherwise be the same one three times, and one of
/// them would fall behind at the next branch. scout-service's approach drafter
/// joined when it was translated.</para>
/// </remarks>
public class DraftErrorTests
{
    /// <summary>What must appear in no message.</summary>
    private const string Secret = "I came from Aleppo in 1998 and I care for my mother.";

    private const string Wish = "please shorter";

    /// <summary>
    /// A connection error names the kind — and does not carry the prompt along.
    /// </summary>
    [Theory]
    [InlineData("profile")]
    [InlineData("advert")]
    [InlineData("approach")]
    public async Task A_connection_error_does_not_carry_the_prompt_along(string page)
    {
        var error = await Catch(
            page,
            new StubHandler(_ => Task.FromException<HttpResponseMessage>(
                new HttpRequestException("no network"))));

        error.Message.Should().Contain("HttpRequestException", "the kind belongs in it");
        WithoutContent(error);
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
    [InlineData("profile")]
    [InlineData("advert")]
    [InlineData("approach")]
    public async Task An_error_response_carries_neither_body_nor_prompt(string page)
    {
        var error = await Catch(page, new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"invalid_request: '{Secret}'")
            })));

        error.Message.Should().Contain("400", "the status code is the kind");
        WithoutContent(error);
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
    [InlineData("profile")]
    [InlineData("advert")]
    [InlineData("approach")]
    public async Task A_timeout_becomes_a_provider_error(string page)
    {
        var error = await Catch(
            page,
            new StubHandler(async token =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            timeLimit: TimeSpan.FromMilliseconds(80));

        // All three services name the kind in the same word since their
        // translation. Either way it is the KIND, never the content.
        error.Message.Should().Contain("timeout");
        WithoutContent(error);
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
    [InlineData("profile")]
    [InlineData("advert")]
    [InlineData("approach")]
    public async Task An_abort_by_the_caller_stays_an_abort(string page)
    {
        using var cancellationToken = new CancellationTokenSource();

        var handler = new StubHandler(async token =>
        {
            await cancellationToken.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var act = async () => await Draft(page, handler, TimeSpan.FromSeconds(30), cancellationToken.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "whoever hangs up reports no provider outage");
    }

    /// <summary>No message carries content — neither the text nor the wish.</summary>
    private static void WithoutContent(Exception error)
    {
        error.Message.Should().NotContain("Aleppo", "no word from the person's text");
        error.Message.Should().NotContain(Secret);
        error.Message.Should().NotContain(Wish, "the wish is content too");
    }

    private static async Task<Exception> Catch(
        string page, StubHandler handler, TimeSpan? timeLimit = null)
    {
        try
        {
            await Draft(page, handler, timeLimit ?? TimeSpan.FromSeconds(30), default);
        }
        catch (Exception error)
        {
            return error;
        }

        throw new InvalidOperationException("no exception was thrown — the test then checks nothing");
    }

    private static Task<string> Draft(
        string page, StubHandler handler, TimeSpan timeLimit, CancellationToken token)
    {
        var factory = new StubFactory(handler);

        if (page == "profile")
        {
            var settings = new ProfileDraft.DraftSettings
            {
                ApiKey = "probe",
                Timeout = timeLimit
            };

            return new ProfileDraft.HttpDrafter(factory, Options.Create(settings))
                .DraftAsync(
                    new ProfilePorts.DraftSituation("Title", Secret, ["Python"], Wish), token);
        }

        if (page == "approach")
        {
            var scout = new ScoutDraft.DraftSettings
            {
                ApiKey = "probe",
                Timeout = timeLimit
            };

            // The context carries no free text of the person, so the secret
            // travels as the headline — the field that does reach the prompt.
            return new ScoutDraft.HttpDrafter(factory, Options.Create(scout))
                .DraftAsync(
                    new ScoutPorts.ApproachSituation(Secret, ["Python"], ["Python"], Wish),
                    token);
        }

        var jobs = new JobsDraft.DraftSettings
        {
            ApiKey = "probe",
            Timeout = timeLimit
        };

        return new JobsDraft.HttpDrafter(factory, Options.Create(jobs))
            .DraftAsync(
                new JobsPorts.AdvertDraft("Title", Secret, ["Python"], "Berlin", Wish),
                token);
    }

    /// <summary>A factory that always hands out the same stub.</summary>
    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>A handler that does what it was given.</summary>
    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> act)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            act(cancellationToken);
    }
}
