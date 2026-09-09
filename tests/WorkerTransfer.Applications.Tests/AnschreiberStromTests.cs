using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.Anschreiben;

namespace WorkerTransfer.Applications.Tests;

/// <summary>
/// Das Modell liefert Token, nicht erst den fertigen Brief.
/// </summary>
/// <remarks>
/// Ohne <c>stream: true</c> und <c>ResponseHeadersRead</c> wartet
/// <c>HttpClient</c> auf das letzte Byte. Ein lokales 7B-Modell braucht dafür
/// oft länger als die Minute, und die Oberfläche zeigt Zeitüberschreitung,
/// obwohl schon geschrieben wurde.
/// </remarks>
public sealed class AnschreiberStromTests
{
    [Fact]
    public async Task OpenAI_SSE_wird_zusammengefügt()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"BETREFF: Bewerbung"}}]}

            data: {"choices":[{"delta":{"content":"\n---\n"}}]}

            data: {"choices":[{"delta":{"content":"Sehr geehrte Damen und Herren."}}]}

            data: [DONE]

            """;

        (await HttpAnschreiber.LiesStromAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(sse)),
                TimeSpan.FromSeconds(5),
                null))
            .Should().Be("BETREFF: Bewerbung\n---\nSehr geehrte Damen und Herren.");
    }

    [Fact]
    public async Task Anthropic_Deltas_werden_zusammengefügt()
    {
        var sse = """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_1"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hallo "}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Welt"}}

            event: message_stop
            data: {"type":"message_stop"}

            """;

        (await HttpAnschreiber.LiesStromAsync(
                "anthropic",
                new MemoryStream(Encoding.UTF8.GetBytes(sse)),
                TimeSpan.FromSeconds(5)))
            .Should().Be("Hallo Welt");
    }

    [Fact]
    public async Task Ein_klassisches_JSON_ohne_Strom_bleibt_lesbar()
    {
        var json = """
            {"choices":[{"message":{"content":"BETREFF: Test\n---\nText."}}]}
            """;

        (await HttpAnschreiber.LiesStromAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(json)),
                TimeSpan.FromSeconds(5)))
            .Should().Be("BETREFF: Test\n---\nText.");
    }

    [Fact]
    public async Task Die_Anfrage_verlangt_einen_Strom()
    {
        var handler = new FangtHandler
        {
            Antwort = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    data: {"choices":[{"delta":{"content":"BETREFF: X\n---\nY"}}]}

                    data: [DONE]
                    """,
                    Encoding.UTF8,
                    "text/event-stream")
            }
        };

        var anschreiber = new HttpAnschreiber(
            new FesteFabrik(handler),
            Options.Create(new Anschreibeneinstellungen()));

        var zugang = new KiZugang(
            "openai_compatible",
            "http://ollama.test/v1/chat/completions",
            "qwen2.5-coder:7b",
            "");

        var kontext = new Anschreibenkontext(
            "Stelle", "Firma", "Ort", "Beschreibung", ["C#"],
            "Anna", "Dev", "Text", ["C#"], [], "de");

        var text = await anschreiber.SchreibeAsync(zugang, kontext);

        text.Should().Be("BETREFF: X\n---\nY");
        handler.Koerper.Should().Contain("\"stream\":true");
        handler.Koerper.Should().Contain("qwen2.5-coder:7b");
    }

    [Fact]
    public async Task Ein_begonnenes_Anschreiben_überlebt_eine_Pause()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"BETREFF: Bewerbung\n---\nSehr geehrte"}}]}

            """;

        (await HttpAnschreiber.LiesStromAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(sse)),
                TimeSpan.FromSeconds(5),
                null))
            .Should().Contain("Sehr geehrte");
    }

    [Fact]
    public async Task Ollama_NDJSON_ohne_data_Praefix_wird_zusammengefügt()
    {
        var ndjson = """
            {"message":{"content":"BETREFF: Bewerbung"}}
            {"message":{"content":"\n---\n"}}
            {"message":{"content":"Sehr geehrte Damen und Herren."}}
            {"message":{"content":""},"done":true}

            """;

        (await HttpAnschreiber.LiesStromAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(ndjson)),
                TimeSpan.FromSeconds(5)))
            .Should().Be("BETREFF: Bewerbung\n---\nSehr geehrte Damen und Herren.");
    }

    [Fact]
    public async Task Der_Fortschritt_wird_pro_Token_gemeldet()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"Hallo"}}]}

            data: {"choices":[{"delta":{"content":" Welt"}}]}

            data: [DONE]

            """;

        var gesehen = new List<string>();

        await HttpAnschreiber.LiesStromAsync(
            "openai_compatible",
            new MemoryStream(Encoding.UTF8.GetBytes(sse)),
            TimeSpan.FromSeconds(5),
            stueck =>
            {
                gesehen.Add(stueck);
                return Task.CompletedTask;
            });

        gesehen.Should().Equal("Hallo", "Hallo Welt");
    }

    [Fact]
    public void OpenAI_Delta_liest_nur_den_Inhalt()
    {
        HttpAnschreiber.LiesDelta(
                "openai_compatible",
                """{"choices":[{"delta":{"content":"Hallo"}}]}""")
            .Should().Be("Hallo");

        HttpAnschreiber.LiesDelta(
                "openai_compatible",
                """{"choices":[{"delta":{"role":"assistant"}}]}""")
            .Should().BeNull();
    }

    private sealed class FesteFabrik(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false);
    }

    private sealed class FangtHandler : HttpMessageHandler
    {
        public required HttpResponseMessage Antwort { get; init; }

        public string? Koerper { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Koerper = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return Antwort;
        }
    }
}
