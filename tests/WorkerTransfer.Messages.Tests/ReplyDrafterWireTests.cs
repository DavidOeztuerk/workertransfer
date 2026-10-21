using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Messages.Infrastructure.Companies;
using WorkerTransfer.Messages.Infrastructure.Draft;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.ServiceDefaults.Ai;

namespace WorkerTransfer.Messages.Tests;

/// <summary>What reaches the provider for a reply draft — measured at a real socket.</summary>
public sealed class ReplyDrafterWireTests
{
    [Fact]
    public async Task The_name_leaves_as_a_placeholder_and_comes_back()
    {
        var received = string.Empty;
        using var provider = Listen(body => received = body, "Liebe Grüße\n[PERSON_NAME]");

        var draft = await Drafter(provider.Port).DraftAsync(
            new ReplyContext(
                Side.Person,
                [new HistoryEntry(Side.Company, "Hallo Anna Muster, passt Ihnen Dienstag?")],
                "Anna Muster", "de"),
            new SubjectId(Guid.NewGuid()), null);

        received.Should().NotContain("Anna").And.NotContain("Muster");
        received.Should().Contain("[PERSON_NAME]");
        draft.Should().Be("Liebe Grüße\nAnna Muster");
    }

    [Fact]
    public async Task The_company_side_uses_the_platform_and_never_asks_for_a_persons_access()
    {
        var received = 0;
        using var provider = Listen(_ => Interlocked.Increment(ref received), "Danke.");

        // Identity is configured to an address nobody answers on: the company
        // side must not even try.
        var drafter = Drafter(provider.Port, identity: "http://127.0.0.1:1");

        var draft = await drafter.DraftAsync(
            new ReplyContext(Side.Company, [], "Anna Muster", "de"), new SubjectId(Guid.NewGuid()), new TenantId(Guid.NewGuid()));

        draft.Should().Be("Danke.");
        received.Should().Be(1);
    }

    [Fact]
    public async Task The_company_side_uses_its_own_key_when_an_admin_stored_one()
    {
        var platform = 0;
        var own = 0;
        using var platformProvider = Listen(_ => Interlocked.Increment(ref platform), "Plattform.");
        using var ownProvider = Listen(_ => Interlocked.Increment(ref own), "Eigener Schlüssel.");

        var access = new FixedCompanyAccess(new AiAccessV1(
            "openai_compatible", $"http://127.0.0.1:{ownProvider.Port}/v1/chat/completions", "own-model", "own-key"));

        var draft = await Drafter(platformProvider.Port, companyAi: access).DraftAsync(
            new ReplyContext(Side.Company, [], "Anna Muster", "de"),
            new SubjectId(Guid.NewGuid()), new TenantId(Guid.NewGuid()));

        draft.Should().Be("Eigener Schlüssel.");
        own.Should().Be(1);
        platform.Should().Be(0);
    }

    [Fact]
    public async Task A_silent_company_lookup_is_refused_not_replaced_by_the_platform()
    {
        var platform = 0;
        using var provider = Listen(_ => Interlocked.Increment(ref platform), "Plattform.");

        var act = () => Drafter(provider.Port, companyAi: new FixedCompanyAccess(null, silent: true)).DraftAsync(
            new ReplyContext(Side.Company, [], "Anna", "de"),
            new SubjectId(Guid.NewGuid()), new TenantId(Guid.NewGuid()));

        await act.Should().ThrowAsync<DraftUnavailable>();
        platform.Should().Be(0, "the company may have chosen against the platform's provider");
    }

    [Fact]
    public async Task Without_a_platform_key_nothing_is_called()
    {
        var act = () => new HttpReplyDrafter(
                Factory(), Options.Create(new DraftSettings()), Options.Create(new IdentitySettings()),
                new NoCompanyAccess(), NullLogger<HttpReplyDrafter>.Instance)
            .DraftAsync(new ReplyContext(Side.Company, [], "Anna", "de"), new SubjectId(Guid.NewGuid()), new TenantId(Guid.NewGuid()));

        await act.Should().ThrowAsync<DraftUnavailable>().WithMessage(DraftUnavailable.NoProvider);
    }

    [Fact]
    public void The_context_carries_nothing_beyond_the_conversation() =>
        typeof(ReplyContext).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(ReplyContext.Writing),
                nameof(ReplyContext.History),
                nameof(ReplyContext.PersonName),
                nameof(ReplyContext.Language));

    [Fact]
    public void The_advice_context_carries_nothing_beyond_the_conversation_and_her_own_facts() =>
        typeof(AdviceContext).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(AdviceContext.History),
                nameof(AdviceContext.PersonName),
                nameof(AdviceContext.Facts),
                nameof(AdviceContext.Language));

    [Fact]
    public void The_advice_rules_keep_the_three_lines() =>
        AdviceContext.Rules.Should().Contain("Kein Rechtsrat")
            .And.Contain("Keine Zahl ueber die Person")
            .And.Contain("Quelle");

    [Fact]
    public void A_silent_source_is_said_not_left_out()
    {
        var prompt = HttpReplyDrafter.AdvicePrompt(
            new AdviceContext([], "Anna", new OwnFacts(null, WishesUnknown: true, null, NoticeUnknown: true), "de"));

        prompt.Should().Contain("Mandat: nicht abrufbar").And.Contain("Fruehester Wechsel: nicht abrufbar");
    }

    [Fact]
    public void Not_stated_is_said_differently_from_not_reachable()
    {
        var prompt = HttpReplyDrafter.AdvicePrompt(
            new AdviceContext([], "Anna", new OwnFacts(null, false, null, false), "de"));

        prompt.Should().Contain("Mandat: keines angelegt").And.Contain("keine Kuendigungsfrist angegeben");
    }

    [Fact]
    public async Task The_advice_masks_her_name_too()
    {
        var received = string.Empty;
        using var provider = Listen(body => received = body, "Lies es genau, [PERSON_NAME].");

        var advice = await Drafter(provider.Port).AdviseAsync(
            new AdviceContext(
                [new HistoryEntry(Side.Company, "Hallo Anna Muster")],
                "Anna Muster", new OwnFacts(null, false, null, false), "de"),
            new SubjectId(Guid.NewGuid()));

        received.Should().NotContain("Anna").And.NotContain("Muster");
        advice.Should().Be("Lies es genau, Anna Muster.");
    }

    [Fact]
    public void The_rules_forbid_judging_the_other_side() =>
        ReplyContext.Rules.Should().Contain("Bewerte die andere Seite nicht")
            .And.Contain("Gesundheitsdaten")
            .And.Contain("Platzhalter");

    private static HttpReplyDrafter Drafter(int port, string identity = "", ICompanyAiAccess? companyAi = null) =>
        new(Factory(),
            Options.Create(new DraftSettings
            {
                ApiKey = "platform-key",
                BaseUrl = $"http://127.0.0.1:{port}/v1/chat/completions",
                Timeout = TimeSpan.FromSeconds(10)
            }),
            Options.Create(new IdentitySettings { BaseUrl = identity, Secret = identity.Length > 0 ? "s" : "" }),
            companyAi ?? new NoCompanyAccess(),
            NullLogger<HttpReplyDrafter>.Instance);

    /// <summary>A company that set no own access up.</summary>
    private sealed class NoCompanyAccess : ICompanyAiAccess
    {
        public Task<AiAccessV1?> AccessAsync(Guid company, CancellationToken cancellationToken = default) =>
            Task.FromResult<AiAccessV1?>(null);
    }

    /// <summary>A company whose admin stored an access — or a lookup that stays silent.</summary>
    private sealed class FixedCompanyAccess(AiAccessV1? access, bool silent = false) : ICompanyAiAccess
    {
        public Task<AiAccessV1?> AccessAsync(Guid company, CancellationToken cancellationToken = default) =>
            silent ? throw new CompanyAiAccessSilent("silent in the test") : Task.FromResult(access);
    }

    private static IHttpClientFactory Factory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddNoeliaEgressPolicy(policy => policy.Allow("127.0.0.1", "localhost"));
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private sealed class Listener(HttpListener inner, int port) : IDisposable
    {
        public int Port { get; } = port;

        public void Dispose() => inner.Close();
    }

    private static Listener Listen(Action<string> seen, string content)
    {
        int port;

        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                using (var reader = new StreamReader(context.Request.InputStream))
                {
                    seen(await reader.ReadToEndAsync());
                }

                var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { message = new { content } } }
                }));

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        });

        return new Listener(listener, port);
    }
}
