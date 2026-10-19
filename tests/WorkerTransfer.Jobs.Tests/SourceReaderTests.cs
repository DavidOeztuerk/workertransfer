using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Domain.Sources;
using WorkerTransfer.Jobs.Infrastructure.Sources;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>
/// The four shapes, read into our words (ADR-0052) — answers shaped like the
/// ones measured on 17.10.2026, no network.
/// </summary>
public sealed class SourceReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Acme = new(Guid.CreateVersion7());

    private static (HttpSourceReader Reader, Wire Wire, Site Site) Reader(
        HttpStatusCode status, string body, string? feed = null, long maxBytes = 16 * 1024 * 1024)
    {
        var wire = new Wire(status, body);
        var site = new Site(feed);
        var reader = new HttpSourceReader(
            new Factory(wire),
            site,
            Options.Create(new SourceSettings { MaxBytes = maxBytes }),
            NullLogger<HttpSourceReader>.Instance);
        return (reader, wire, site);
    }

    private static JobSource Board(SourceKind kind) => JobSource.Board(Acme, kind, "acme", PublishMode.Draft, Now);

    private static IReadOnlyList<ExternalAdvert> Adverts(SourceRead read) =>
        read.Should().BeOfType<SourceRead.Adverts>().Subject.Items;

    [Fact]
    public async Task Greenhouse_escapes_its_html_once_more_and_says_the_workplace_in_metadata()
    {
        const string body = """
            {"jobs":[{"id":4012345,"title":"Welder","absolute_url":"https://stripe.com/jobs/4012345",
              "location":{"name":"Berlin"},
              "content":"&lt;p&gt;We weld &amp;amp; build.&lt;/p&gt;&lt;ul&gt;&lt;li&gt;MIG&lt;/li&gt;&lt;/ul&gt;",
              "metadata":[{"name":"Workplace Type","value":"Hybrid"},{"name":"Employment Type","value":"Part-time"}]}]}
            """;
        var (reader, wire, _) = Reader(HttpStatusCode.OK, body);

        var advert = Adverts(await reader.ReadAsync(Board(SourceKind.Greenhouse))).Single();

        wire.Asked!.ToString().Should().Be("https://boards-api.greenhouse.io/v1/boards/acme/jobs?content=true");
        advert.ExternalId.Should().Be("4012345");
        advert.Description.Should().Be("We weld & build.\nMIG");
        advert.Location.Should().Be("Berlin");
        advert.Remote.Should().Be(RemoteMode.Hybrid);
        advert.Employment.Should().Be(EmploymentKind.PartTime);
        advert.Url.Should().Be("https://stripe.com/jobs/4012345");
    }

    [Fact]
    public async Task Lever_brings_plain_text_lists_and_a_workplace_type()
    {
        const string body = """
            [{"id":"5f3c-ab","text":"Fitter","descriptionPlain":"Build things.",
              "lists":[{"text":"Requirements","content":"<li>MIG</li><li>TIG</li>"}],
              "additionalPlain":"","workplaceType":"remote",
              "categories":{"location":"Hamburg","commitment":"Full-time"},
              "hostedUrl":"https://jobs.lever.co/acme/5f3c-ab"}]
            """;
        var (reader, wire, _) = Reader(HttpStatusCode.OK, body);

        var advert = Adverts(await reader.ReadAsync(Board(SourceKind.Lever))).Single();

        wire.Asked!.ToString().Should().Be("https://api.lever.co/v0/postings/acme?mode=json");
        advert.Title.Should().Be("Fitter");
        advert.Description.Should().Be("Build things.\n\nRequirements\nMIG\nTIG");
        advert.Remote.Should().Be(RemoteMode.Full);
        advert.Location.Should().Be("Hamburg");
    }

    [Fact]
    public async Task Personio_answers_xml_on_the_companys_own_host()
    {
        const string body = """
            <?xml version="1.0" encoding="UTF-8"?>
            <workzag-jobs><position><id>42</id><name>Elektriker</name><office>München</office>
              <department>Service</department><schedule>full-time</schedule><employmentType>permanent</employmentType>
              <jobDescriptions><jobDescription><name>Aufgaben</name><value>&lt;p&gt;Schalten&lt;/p&gt;</value></jobDescription></jobDescriptions>
            </position><position><id>43</id><name>Azubi</name><office>München</office>
              <schedule>full-time</schedule><employmentType>trainee</employmentType><jobDescriptions/></position></workzag-jobs>
            """;
        var (reader, wire, _) = Reader(HttpStatusCode.OK, body);

        var adverts = Adverts(await reader.ReadAsync(Board(SourceKind.Personio)));

        wire.Asked!.ToString().Should().Be("https://acme.jobs.personio.de/xml?language=de");
        adverts.Should().HaveCount(2);
        adverts[0].Description.Should().Be("Aufgaben\nSchalten");
        adverts[0].Url.Should().Be("https://acme.jobs.personio.de/job/42");
        adverts[1].Employment.Should().Be(EmploymentKind.Internship);
        adverts[1].Description.Should().Contain("https://acme.jobs.personio.de/job/43",
            "an empty description says where the advert is written in full");
    }

    [Fact]
    public async Task A_feed_is_a_list_of_schema_org_job_postings_from_the_companys_own_site()
    {
        const string feed = """
            [{"@type":"JobPosting","title":"Tischler","description":"<p>Holz</p>",
              "identifier":{"@type":"PropertyValue","value":"t-1"},"url":"https://acme.example/jobs/t-1",
              "jobLocation":{"@type":"Place","address":{"addressLocality":"Köln"}},"employmentType":"PART_TIME"},
             {"@type":"Organization","name":"Acme"}]
            """;
        var (reader, wire, site) = Reader(HttpStatusCode.OK, "unused", feed);
        var source = JobSource.Feed(Acme, "https://acme.example/jobs.json", "acme.example", PublishMode.Draft, Now);

        var advert = Adverts(await reader.ReadAsync(source)).Single();

        site.Asked.Should().Be("https://acme.example/jobs.json", "a feed is read with the public-only client");
        wire.Asked.Should().BeNull();
        advert.ExternalId.Should().Be("t-1");
        advert.Description.Should().Be("Holz");
        advert.Location.Should().Be("Köln");
        advert.Employment.Should().Be(EmploymentKind.PartTime);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}", "not_found")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "unreachable")]
    [InlineData(HttpStatusCode.OK, "{not json", "unreadable")]
    [InlineData(HttpStatusCode.OK, """{"unexpected":true}""", "unreadable")]
    public async Task A_failure_is_a_word_never_the_answer(HttpStatusCode status, string body, string word)
    {
        var (reader, _, _) = Reader(status, body);

        var read = await reader.ReadAsync(Board(SourceKind.Greenhouse));

        read.Should().BeOfType<SourceRead.Failed>().Which.Outcome.Should().Be(word);
    }

    [Fact]
    public async Task An_answer_heavier_than_the_limit_is_not_read()
    {
        var (reader, _, _) = Reader(HttpStatusCode.OK, $$"""{"jobs":[{"title":"{{new string('x', 4096)}}"}]}""", maxBytes: 1024);

        var read = await reader.ReadAsync(Board(SourceKind.Greenhouse));

        read.Should().BeOfType<SourceRead.Failed>().Which.Outcome.Should().Be("too_large");
    }

    /// <summary>Answers every request the same, and remembers the address.</summary>
    private sealed class Wire(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Site(string? page) : ICompanySite
    {
        public string? Asked { get; private set; }

        public Task<string?> PageAsync(string address, CancellationToken cancellationToken = default)
        {
            Asked = address;
            return Task.FromResult(page);
        }
    }
}
