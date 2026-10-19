using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.CoverLetter;

namespace WorkerTransfer.Applications.Tests;

/// <summary>
/// The provider never reads the person's name or employers; the person reads
/// them in the letter (VISION-FRAGEN E2).
/// </summary>
/// <remarks>
/// Measured at the wire, with a real socket: what the listener received is
/// what the provider would have received.
/// </remarks>
public sealed class CoverLetterPlaceholderTests
{
    private static readonly CoverLetterContext Context = new(
        "Schweißer", "Stahlbau Nord GmbH", "Kiel", "Stahlbau Nord GmbH sucht Verstärkung.", ["WIG"],
        "Anna Muster", "Schweißerin", "Ich bin Anna und schweiße seit Jahren.", ["WIG"],
        [new CareerStation("Siemens Energy", "Schweißerin", "2019–2024", "Bei Siemens Energy Rohre.", ["WIG"])],
        "de");

    [Fact]
    public async Task Name_company_and_employer_leave_as_placeholders_and_come_back()
    {
        var received = string.Empty;
        const string answer =
            "BETREFF: Bewerbung bei [COMPANY]\n---\nBei [EMPLOYER_1] habe ich gelernt.\n[PERSON_NAME]";

        using var provider = Listen(body => received = body, answer);
        var writer = Writer();

        var fragments = new List<string>();
        var letter = await writer.WriteAsync(
            Access(provider.Port), Context,
            fragment => { fragments.Add(fragment); return Task.CompletedTask; });

        received.Should().NotContainAny("Anna", "Muster", "Siemens", "Stahlbau Nord");
        received.Should().Contain("[PERSON_NAME]").And.Contain("[EMPLOYER_1]").And.Contain("[COMPANY]");

        letter.Should().Be(
            "BETREFF: Bewerbung bei Stahlbau Nord GmbH\n---\nBei Siemens Energy habe ich gelernt.\nAnna Muster");
        fragments.Should().NotBeEmpty().And.OnlyContain(fragment => !fragment.Contains("[PERSON_NAME]"));
    }

    [Fact]
    public async Task A_revision_masks_the_earlier_letter_and_the_comments_too()
    {
        var received = string.Empty;
        using var provider = Listen(body => received = body, "BETREFF: x\n---\n[PERSON_NAME]");

        await Writer().ReviseAsync(
            Access(provider.Port), Context, "Bewerbung bei Stahlbau Nord GmbH",
            "Mit Grüßen, Anna Muster", ["Erwähne Siemens Energy früher"]);

        received.Should().NotContainAny("Anna", "Muster", "Siemens", "Stahlbau Nord");
    }

    private static HttpCoverLetterWriter Writer()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddNoeliaEgressPolicy(policy => policy.Allow("127.0.0.1", "localhost"));

        return new HttpCoverLetterWriter(
            services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            Options.Create(new CoverLetterSettings { Timeout = TimeSpan.FromSeconds(10) }));
    }

    private static AiAccess Access(int port) =>
        new("openai_compatible", $"http://127.0.0.1:{port}/v1/chat/completions", "a-model", string.Empty);

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

                var body = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new
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
