using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.ServiceDefaults.Ai;
using WorkerTransfer.ServiceDefaults.Roles;
using JobsDraft = WorkerTransfer.Jobs.Infrastructure.Draft;
using JobsPorts = WorkerTransfer.Jobs.Application.Ports;
using ScoutDraft = WorkerTransfer.Scout.Infrastructure.Draft;
using ScoutPorts = WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// A company's own key drafts instead of the platform's model — measured with
/// the real lookup against a recorded internal door (ADR-0051, Bauliste 6).
/// </summary>
/// <remarks>
/// The advert help and the scout approach, side by side: the same rule in two
/// services is two chances to break it. The company replies carry it in
/// messages-service's own suite.
/// </remarks>
public sealed class CompanyKeyTests
{
    private const string Platform = "http://host.docker.internal:11434/v1/chat/completions";

    private static readonly AiAccessV1 OwnKey =
        new("anthropic", "https://api.anthropic.com/v1/messages", "claude-opus-5-5", "company-key");

    public static TheoryData<string> Seams => ["jobs", "scout"];

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task The_companys_own_key_drafts_instead_of_the_platform_model(string seam)
    {
        var recorder = new Recorder(OwnKey);
        var company = Guid.CreateVersion7();

        (await Draft(seam, recorder, company)).Should().Be("Entwurf");

        recorder.ModelCall!.RequestUri!.Host.Should().Be("api.anthropic.com");
        recorder.ModelCall.Headers.GetValues("x-api-key").Should().Equal("company-key");
        recorder.DoorPath.Should().Be($"/internal/companies/{company}/ai");
    }

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task Without_an_own_key_the_platform_model_drafts(string seam)
    {
        var recorder = new Recorder(new AiAccessV1("none", string.Empty, string.Empty, string.Empty));

        (await Draft(seam, recorder, Guid.CreateVersion7())).Should().Be("Entwurf");

        recorder.ModelCall!.RequestUri!.Host.Should().Be("host.docker.internal");
    }

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task An_own_key_drafts_even_when_the_platform_has_none(string seam)
    {
        var recorder = new Recorder(OwnKey);

        (await Draft(seam, recorder, Guid.CreateVersion7(), platformKey: string.Empty)).Should().Be("Entwurf");

        recorder.ModelCall!.RequestUri!.Host.Should().Be("api.anthropic.com");
    }

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task Neither_own_key_nor_platform_says_so_and_calls_nobody(string seam)
    {
        var recorder = new Recorder(new AiAccessV1("none", string.Empty, string.Empty, string.Empty));

        var act = () => Draft(seam, recorder, Guid.CreateVersion7(), platformKey: string.Empty);

        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Be("No draft provider is set up.");
        recorder.ModelCall.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task A_silent_identity_service_is_refused_not_replaced(string seam)
    {
        var recorder = new Recorder(own: null, door: HttpStatusCode.ServiceUnavailable);

        var act = () => Draft(seam, recorder, Guid.CreateVersion7());

        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("identity-service");
        recorder.ModelCall.Should().BeNull("the company may have chosen against the platform's provider");
    }

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task A_refused_secret_is_not_read_as_no_key(string seam)
    {
        // The door answers every company with 200; 404 means only a wrong
        // secret. Read as "none", a mis-wired secret would send every company's
        // drafts to the platform's provider without anybody noticing.
        var recorder = new Recorder(own: null, door: HttpStatusCode.NotFound);

        var act = () => Draft(seam, recorder, Guid.CreateVersion7());

        await act.Should().ThrowAsync<Exception>();
        recorder.ModelCall.Should().BeNull();
    }

    [Fact]
    public async Task Without_identity_configured_there_is_no_own_access()
    {
        var recorder = new Recorder(OwnKey);
        var lookup = new HttpCompanyAiAccess(
            new Factory(recorder), Options.Create(new CompanyRoleSettings()), new CompanyAiHosts(),
            NullLogger<HttpCompanyAiAccess>.Instance);

        (await lookup.AccessAsync(Guid.CreateVersion7())).Should().BeNull();
        recorder.DoorPath.Should().BeNull("nobody is configured to be asked");
    }

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task An_own_server_drafts_without_a_key_through_the_public_only_client(string seam)
    {
        var recorder = new Recorder(new AiAccessV1(
            "openai_compatible", "https://llm.acme.example/v1/chat/completions", "qwen2.5:14b", string.Empty));
        var hosts = new CompanyAiHosts();

        (await Draft(seam, recorder, Guid.CreateVersion7(), hosts: hosts)).Should().Be("Entwurf");

        recorder.ModelCall!.RequestUri!.Host.Should().Be("llm.acme.example");
        recorder.ModelClient.Should().Be(HttpCompanyAiAccess.ModelClientName,
            "the company's server lies under its domain, and that domain must not point inside");
        hosts.Knows("llm.acme.example").Should().BeTrue("the egress boundary learns the host just before the call");
    }

    [Theory]
    [MemberData(nameof(Seams))]
    public async Task The_platform_model_keeps_its_own_client(string seam)
    {
        var recorder = new Recorder(new AiAccessV1("none", string.Empty, string.Empty, string.Empty));

        (await Draft(seam, recorder, Guid.CreateVersion7())).Should().Be("Entwurf");

        recorder.ModelClient.Should().NotBe(HttpCompanyAiAccess.ModelClientName,
            "the platform's Ollama lies inside on purpose");
    }

    [Fact]
    public async Task An_address_that_is_not_https_is_not_used()
    {
        var recorder = new Recorder(new AiAccessV1(
            "openai_compatible", "http://llm.acme.example/v1/chat/completions", "qwen2.5:14b", string.Empty));
        var hosts = new CompanyAiHosts();
        var lookup = new HttpCompanyAiAccess(
            new Factory(recorder),
            Options.Create(new CompanyRoleSettings { BaseUrl = "http://identity-service:8001", Secret = "s" }),
            hosts, NullLogger<HttpCompanyAiAccess>.Instance);

        (await lookup.AccessAsync(Guid.CreateVersion7())).Should().BeNull();
        hosts.All.Should().BeEmpty();
    }

    [Fact]
    public void The_egress_boundary_lets_through_only_handed_out_hosts_over_https()
    {
        var hosts = new CompanyAiHosts();
        var egress = new CompanyAiEgress(new Strict("identity-service"), hosts);

        egress.IsAllowed(new Uri("http://identity-service:8001/internal/x")).Should().BeTrue("the inner policy still decides");
        egress.IsAllowed(new Uri("https://llm.acme.example/v1/chat/completions")).Should().BeFalse("not handed out yet");

        hosts.Remember("llm.acme.example");

        egress.IsAllowed(new Uri("https://llm.acme.example/v1/chat/completions")).Should().BeTrue();
        egress.IsAllowed(new Uri("http://llm.acme.example/v1/chat/completions")).Should().BeFalse("only https");
        egress.IsAllowed(new Uri("https://other.acme.example/v1/chat/completions")).Should().BeFalse("the host, not the domain");
        egress.DeclaredHosts.Should().Contain(["identity-service", "llm.acme.example"]);
    }

    private static Task<string> Draft(
        string seam, Recorder recorder, Guid company, string platformKey = "ollama", CompanyAiHosts? hosts = null)
    {
        var factory = new Factory(recorder);
        var lookup = new HttpCompanyAiAccess(
            factory,
            Options.Create(new CompanyRoleSettings { BaseUrl = "http://identity-service:8001", Secret = "s" }),
            hosts ?? new CompanyAiHosts(),
            NullLogger<HttpCompanyAiAccess>.Instance);

        if (seam == "jobs")
        {
            return new JobsDraft.HttpDrafter(
                    factory,
                    Options.Create(new JobsDraft.DraftSettings { ApiKey = platformKey, BaseUrl = Platform }),
                    lookup)
                .DraftAsync(
                    new JobsPorts.AdvertDraft("Schweißer", "Text", ["MIG"], "Berlin", "kurz"),
                    new TenantId(company));
        }

        return new ScoutDraft.HttpDrafter(
                factory,
                Options.Create(new ScoutDraft.DraftSettings { ApiKey = platformKey, BaseUrl = Platform }),
                lookup)
            .DraftAsync(
                new ScoutPorts.ApproachSituation("Schweißer", ["MIG"], ["MIG"], "kurz"),
                new TenantId(company));
    }

    /// <summary>Answers the internal door and the model, and remembers both.</summary>
    private sealed class Recorder(AiAccessV1? own, HttpStatusCode door = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? ModelCall { get; private set; }

        public string? DoorPath { get; private set; }

        /// <summary>The name of the last client handed out — the model's, once it was called.</summary>
        public string? ModelClient { get; private set; }

        public string? LastClient { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/internal/", StringComparison.Ordinal))
            {
                DoorPath = request.RequestUri.AbsolutePath;
                return Task.FromResult(door == HttpStatusCode.OK
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(own) }
                    : new HttpResponseMessage(door));
            }

            ModelCall = request;
            ModelClient = LastClient;

            var body = request.RequestUri.Host == "api.anthropic.com"
                ? """{"content":[{"type":"text","text":"Entwurf"}]}"""
                : """{"choices":[{"message":{"content":"Entwurf"}}]}""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class Factory(Recorder handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            handler.LastClient = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    /// <summary>An enforcing policy that knows one host.</summary>
    private sealed class Strict(string host) : IEgressPolicy
    {
        public bool IsAllowed(Uri destination) => destination.Host == host;

        public IReadOnlyCollection<string> DeclaredHosts => [host];

        public bool IsEnforcing => true;
    }
}
