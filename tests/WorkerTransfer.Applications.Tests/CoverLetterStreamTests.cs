using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.CoverLetter;

namespace WorkerTransfer.Applications.Tests;

/// <summary>
/// The model delivers tokens, not only the finished letter.
/// </summary>
/// <remarks>
/// Without <c>stream: true</c> and <c>ResponseHeadersRead</c>
/// <c>HttpClient</c> waits for the last byte. A local 7B model often needs longer
/// than the minute for it, and the interface shows a timeout although it has
/// already written.
/// <para>
/// The <c>BETREFF:</c> marker in the samples is the German one the prompt asks
/// for (<c>CoverLetterContext.Rules</c>).
/// </para>
/// </remarks>
public sealed class CoverLetterStreamTests
{
    [Fact]
    public async Task OpenAI_SSE_is_joined()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"BETREFF: Application"}}]}

            data: {"choices":[{"delta":{"content":"\n---\n"}}]}

            data: {"choices":[{"delta":{"content":"Dear Sir or Madam."}}]}

            data: [DONE]

            """;

        (await HttpCoverLetterWriter.ReadStreamAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(sse)),
                TimeSpan.FromSeconds(5),
                null))
            .Should().Be("BETREFF: Application\n---\nDear Sir or Madam.");
    }

    [Fact]
    public async Task Anthropic_deltas_are_joined()
    {
        var sse = """
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_1"}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello "}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"world"}}

            event: message_stop
            data: {"type":"message_stop"}

            """;

        (await HttpCoverLetterWriter.ReadStreamAsync(
                "anthropic",
                new MemoryStream(Encoding.UTF8.GetBytes(sse)),
                TimeSpan.FromSeconds(5)))
            .Should().Be("Hello world");
    }

    [Fact]
    public async Task A_classic_JSON_without_a_stream_stays_readable()
    {
        var json = """
            {"choices":[{"message":{"content":"BETREFF: Test\n---\nText."}}]}
            """;

        (await HttpCoverLetterWriter.ReadStreamAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(json)),
                TimeSpan.FromSeconds(5)))
            .Should().Be("BETREFF: Test\n---\nText.");
    }

    [Fact]
    public async Task The_request_asks_for_a_stream()
    {
        var handler = new CapturingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
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

        var writer = new HttpCoverLetterWriter(
            new FixedFactory(handler),
            Options.Create(new CoverLetterSettings()));

        var access = new AiAccess(
            "openai_compatible",
            "http://ollama.example.test/v1/chat/completions",
            "qwen2.5-coder:7b",
            "");

        var context = new CoverLetterContext(
            "Job", "Company", "Place", "Description", ["C#"],
            "Anna", "Dev", "Text", ["C#"], [], "de");

        var text = await writer.WriteAsync(access, context);

        text.Should().Be("BETREFF: X\n---\nY");
        handler.Body.Should().Contain("\"stream\":true");
        handler.Body.Should().Contain("qwen2.5-coder:7b");
    }

    [Fact]
    public async Task A_begun_cover_letter_survives_a_pause()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"BETREFF: Application\n---\nDear Sir"}}]}

            """;

        (await HttpCoverLetterWriter.ReadStreamAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(sse)),
                TimeSpan.FromSeconds(5),
                null))
            .Should().Contain("Dear Sir");
    }

    [Fact]
    public async Task Ollama_NDJSON_without_a_data_prefix_is_joined()
    {
        var ndjson = """
            {"message":{"content":"BETREFF: Application"}}
            {"message":{"content":"\n---\n"}}
            {"message":{"content":"Dear Sir or Madam."}}
            {"message":{"content":""},"done":true}

            """;

        (await HttpCoverLetterWriter.ReadStreamAsync(
                "openai_compatible",
                new MemoryStream(Encoding.UTF8.GetBytes(ndjson)),
                TimeSpan.FromSeconds(5)))
            .Should().Be("BETREFF: Application\n---\nDear Sir or Madam.");
    }

    [Fact]
    public async Task Progress_is_reported_per_token()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"Hello"}}]}

            data: {"choices":[{"delta":{"content":" world"}}]}

            data: [DONE]

            """;

        var seen = new List<string>();

        await HttpCoverLetterWriter.ReadStreamAsync(
            "openai_compatible",
            new MemoryStream(Encoding.UTF8.GetBytes(sse)),
            TimeSpan.FromSeconds(5),
            piece =>
            {
                seen.Add(piece);
                return Task.CompletedTask;
            });

        seen.Should().Equal("Hello", "Hello world");
    }

    [Fact]
    public void An_OpenAI_delta_reads_only_the_content()
    {
        HttpCoverLetterWriter.ReadDelta(
                "openai_compatible",
                """{"choices":[{"delta":{"content":"Hello"}}]}""")
            .Should().Be("Hello");

        HttpCoverLetterWriter.ReadDelta(
                "openai_compatible",
                """{"choices":[{"delta":{"role":"assistant"}}]}""")
            .Should().BeNull();
    }

    private sealed class FixedFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public required HttpResponseMessage Response { get; init; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return Response;
        }
    }
}
