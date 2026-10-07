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
            new SubjectId(Guid.NewGuid()));

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
            new ReplyContext(Side.Company, [], "Anna Muster", "de"), new SubjectId(Guid.NewGuid()));

        draft.Should().Be("Danke.");
        received.Should().Be(1);
    }

    [Fact]
    public async Task Without_a_platform_key_nothing_is_called()
    {
        var act = () => new HttpReplyDrafter(
                Factory(), Options.Create(new DraftSettings()), Options.Create(new IdentitySettings()),
                NullLogger<HttpReplyDrafter>.Instance)
            .DraftAsync(new ReplyContext(Side.Company, [], "Anna", "de"), new SubjectId(Guid.NewGuid()));

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
    public void The_rules_forbid_judging_the_other_side() =>
        ReplyContext.Rules.Should().Contain("Bewerte die andere Seite nicht")
            .And.Contain("Gesundheitsdaten")
            .And.Contain("Platzhalter");

    private static HttpReplyDrafter Drafter(int port, string identity = "") =>
        new(Factory(),
            Options.Create(new DraftSettings
            {
                ApiKey = "platform-key",
                BaseUrl = $"http://127.0.0.1:{port}/v1/chat/completions",
                Timeout = TimeSpan.FromSeconds(10)
            }),
            Options.Create(new IdentitySettings { BaseUrl = identity, Secret = identity.Length > 0 ? "s" : "" }),
            NullLogger<HttpReplyDrafter>.Instance);

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
