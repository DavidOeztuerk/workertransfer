using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Contracts.Identity;
using ProfileDraft = WorkerTransfer.Profile.Infrastructure.Draft;
using ProfilePorts = WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// A person's own key drafts instead of the platform's model — and a silent
/// identity-service is refused, never replaced by the platform (ADR-0051).
/// </summary>
/// <remarks>
/// Measured at the profile help, the seam that learned it last; the cover
/// letter and the reply drafts carry the same rule in their own suites.
/// </remarks>
public sealed class OwnKeyTests
{
    private const string Platform = "http://host.docker.internal:11434/v1/chat/completions";

    private static ProfileDraft.HttpDrafter Drafter(Recorder recorder) =>
        new(
            new Factory(recorder),
            Options.Create(new ProfileDraft.DraftSettings { ApiKey = "ollama", BaseUrl = Platform }),
            Options.Create(new ProfileDraft.IdentitySettings { BaseUrl = "http://identity-service:8001", Secret = "s" }));

    private static Task<string> Draft(ProfileDraft.HttpDrafter drafter) =>
        drafter.DraftAsync(
            new ProfilePorts.DraftSituation("Title", "Text", ["MIG"], "kurz"),
            new SubjectId(Guid.CreateVersion7()));

    [Fact]
    public async Task An_own_key_drafts_instead_of_the_platform_model()
    {
        var recorder = new Recorder(new AiAccessV1("anthropic", "https://api.anthropic.com/v1/messages", "claude-opus-5-5", "own-key"));

        (await Draft(Drafter(recorder))).Should().Be("Entwurf");

        recorder.ModelCall!.RequestUri!.Host.Should().Be("api.anthropic.com");
        recorder.ModelCall.Headers.GetValues("x-api-key").Should().Equal("own-key");
    }

    [Fact]
    public async Task Without_an_own_key_the_platform_model_drafts()
    {
        var recorder = new Recorder(new AiAccessV1("none", string.Empty, string.Empty, string.Empty));

        (await Draft(Drafter(recorder))).Should().Be("Entwurf");

        recorder.ModelCall!.RequestUri!.Host.Should().Be("host.docker.internal");
    }

    [Fact]
    public async Task A_silent_identity_service_is_refused_not_replaced()
    {
        var recorder = new Recorder(own: null, identitySilent: true);

        var act = () => Draft(Drafter(recorder));

        await act.Should().ThrowAsync<ProfilePorts.DraftUnavailable>();
        recorder.ModelCall.Should().BeNull("her text must not go to a provider she may have chosen against");
    }

    /// <summary>Answers the internal door and the model, and remembers the model call.</summary>
    private sealed class Recorder(AiAccessV1? own, bool identitySilent = false) : HttpMessageHandler
    {
        public HttpRequestMessage? ModelCall { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/internal/account/", StringComparison.Ordinal))
            {
                return identitySilent
                    ? throw new HttpRequestException("identity-service is silent")
                    : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(own) });
            }

            ModelCall = request;

            var body = request.RequestUri.Host == "api.anthropic.com"
                ? """{"content":[{"type":"thinking","thinking":""},{"type":"text","text":"Entwurf"}]}"""
                : """{"choices":[{"message":{"content":"Entwurf"}}]}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
