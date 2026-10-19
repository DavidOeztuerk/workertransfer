using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Infrastructure.Persistence;
using WorkerTransfer.Resume.Infrastructure.TextRecognition;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Resume.Tests;

/// <summary>
/// Counts how often something was really read.
/// </summary>
/// <remarks>
/// <strong>The counter is the point, not the convenience.</strong> The promise
/// reads "without a request no document is read", and it could also be read off
/// the empty index — more weakly: a read that runs and throws its result away
/// has still read the reference. What is measured is therefore the
/// <em>call</em>.
/// <para>
/// It wraps the REAL recogniser instead of replacing it: a fake that throws its
/// inputs away has already hidden a real defect three times in this tree.
/// </para>
/// </remarks>
public sealed class CountingRecogniser(PdfTextRecogniser real) : ITextRecogniser
{
    private int _calls;

    /// <summary>How often <see cref="Read"/> was called.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <inheritdoc />
    public RecognisedText Read(ReadOnlyMemory<byte> content, string contentType)
    {
        Interlocked.Increment(ref _calls);
        return real.Read(content, contentType);
    }
}

/// <summary>
/// The electrician uploads his reference, presses a button, and the platform
/// tells him what stands in it (PBI-7, ADR-0043).
/// </summary>
/// <remarks>
/// <para><strong>With real PDFs and the real recogniser.</strong> Half the
/// promise concerns the file and not the row: that nothing can be read from a
/// picture, that the spelling in the reference becomes the name on the profile,
/// that a break in the middle of a word breaks nothing. A recogniser fake would
/// confirm all of that without checking any of it.</para>
///
/// <para>The PDFs come into being in the test with <c>PdfDocumentBuilder</c> —
/// the same library, the other direction. That is no circular argument but the
/// only way to check what a typeset document yields without a checked-in binary
/// file.</para>
///
/// <para>The text inside the PDFs stays German on purpose: the references this
/// platform reads are German, and the vocabulary is tested against the
/// spellings found in them.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class RecognitionJourneyTests(Postgres postgres) : IAsyncLifetime
{
    /// <summary>A real PNG — a picture, and no text stands in it.</summary>
    private static readonly byte[] APicture =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly string _documentStore = Path.Combine(
        Path.GetTempPath(), "wt-recognition-" + Guid.CreateVersion7().ToString("N"));

    private readonly ProbeGate _gate = new();
    private readonly ProbeDelivery _delivery = new();
    private CountingRecogniser _counter = null!;
    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_documentStore);

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:resume", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("DocumentStore:Folder", _documentStore);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _gate));
                services.Replace(ServiceDescriptor.Scoped<IDelivery>(_ => _delivery));
                services.Replace(ServiceDescriptor.Singleton<ITextRecogniser>(provider =>
                    new CountingRecogniser(
                        new PdfTextRecogniser(
                            provider.GetRequiredService<
                                Microsoft.Extensions.Logging.ILogger<PdfTextRecogniser>>()))));
            });
        });

        // Build the container and FETCH the counter instead of assigning it on
        // the side during registration: the factory would otherwise run only at
        // the first call, and exactly the test that measures FIRST would have no
        // `_counter`.
        _ = _service.CreateClient();
        _counter = (CountingRecogniser)_service.Services.GetRequiredService<ITextRecogniser>();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();

        if (Directory.Exists(_documentStore))
        {
            Directory.Delete(_documentStore, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>A typeset PDF with these lines in it.</summary>
    /// <remarks>
    /// Every line is drawn separately, exactly as in a real document — and with
    /// it the case in which PdfPig's <c>Text</c> joins two blocks without a
    /// space.
    /// </remarks>
    private static byte[] APdfWith(params string[] rows)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var y = 760.0;

        foreach (var row in rows)
        {
            page.AddText(row, 12, new PdfPoint(40, y), font);
            y -= 20;
        }

        return builder.Build();
    }

    private HttpClient AsPerson(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));

        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Upload(
        HttpClient browser, byte[] content, string fileName, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(name), "name");
        // The wire word for a reference.
        form.Add(new StringContent("zeugnis"), "kind");

        return browser.PostAsync("/resumes/me/documents", form);
    }

    /// <summary>The words that were found for a document.</summary>
    private static string[] TermsOf(JsonElement state, string name) =>
        [.. state.EnumerateArray()
            .First(entry => entry.GetProperty("name").GetString() == name)
            .GetProperty("terms").EnumerateArray()
            .Select(word => word.GetString()!)];

    private async Task<int> TermRowsFor(Guid subject)
    {
        await using var source = ResumeDbContextFactory.DataSource(postgres.ConnectionString);
        await using var context = new ResumeDbContext(
            (DbContextOptions<ResumeDbContext>)ResumeDbContextFactory.Configure(
                new DbContextOptionsBuilder<ResumeDbContext>(), source).Options);

        return await context.Terms.CountAsync(row => row.SubjectId == subject);
    }

    // -----------------------------------------------------------------------
    // WITHOUT A REQUEST NOTHING IS READ (ADR-0004)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <strong>Uploading does not read.</strong>
    /// </summary>
    /// <remarks>
    /// The promise for whose sake there is a button here at all. "On request"
    /// alone would be toothless — a read at upload would formally be requested
    /// too, namely by the upload. What is meant is what ADR-0004 already says
    /// for GitHub: looking once when asked is something other than watching
    /// someone permanently.
    /// <para>
    /// Measured in three places, because each on its own would be too weak: the
    /// recogniser's <em>counter</em> (nothing was read), the <em>answer</em>
    /// (<c>read_at</c> is null, so "never read") and the <em>table</em> (no row
    /// stands in it).
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Without_a_request_no_document_is_read()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);

        await Upload(hers, APdfWith("Herr Meier ist Schweissfachmann DVS."), "r.pdf", "Reference");

        _counter.Calls.Should().Be(0, "the upload does not call the recogniser");

        var state = await Json(await hers.GetAsync("/resumes/me/documents/terms"));

        state.GetArrayLength().Should().Be(1);
        state[0].GetProperty("read_at").ValueKind.Should().Be(JsonValueKind.Null);
        state[0].GetProperty("terms").GetArrayLength().Should().Be(0);

        // And the query itself does not read either — otherwise opening the
        // profile page would be exactly the background run the button avoids.
        _counter.Calls.Should().Be(0);
        (await TermRowsFor(anna)).Should().Be(0);
    }

    /// <summary>
    /// <strong>One click, and "Schweißfachmann" stands in the reference.</strong>
    /// </summary>
    /// <remarks>
    /// The acceptance of PBI-7, whole: a PDF is uploaded with the spelling
    /// without the eszett, and back comes the canonical name. That is the
    /// vocabulary at work — it <em>renames</em> and infers nothing (ADR-0023):
    /// that somebody can also weld does not stand here.
    /// </remarks>
    [Fact]
    public async Task One_click_finds_the_words_in_the_reference()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        await Upload(
            hers,
            APdfWith(
                "Zeugnis der Metallbau Nord GmbH",
                "Herr Meier ist Schweissfachmann DVS",
                "und bedient CNC-Maschinen im Tagesbetrieb."),
            "reference.pdf",
            "Employment reference North");

        var read = await hers.PostAsync("/resumes/me/documents/read", null);

        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var state = await Json(read);

        state[0].GetProperty("read_at").ValueKind.Should().NotBe(JsonValueKind.Null);
        state[0].GetProperty("has_text").GetBoolean().Should().BeTrue();
        TermsOf(state, "Employment reference North")
            .Should().Contain("Schweißfachmann").And.Contain("CNC");

        _counter.Calls.Should().Be(1, "exactly one document, exactly one read");
    }

    /// <summary>
    /// <strong>No second word follows from a term.</strong>
    /// </summary>
    /// <remarks>
    /// The counter-probe to the temptation. Whoever reads "Schweißfachmann"
    /// knows this human being welds — and still must not write it down: that
    /// would be a statement about them they never made, at a place where they
    /// cannot object. The same check as
    /// <c>From_MIG_MAG_no_welding_follows</c>, one layer up.
    /// </remarks>
    [Fact]
    public async Task Nothing_is_inferred_from_the_term()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        await Upload(hers, APdfWith("Schweissfachmann DVS, mit Urkunde."), "r.pdf", "Reference");
        var state = await Json(await hers.PostAsync("/resumes/me/documents/read", null));

        TermsOf(state, "Reference").Should().Equal("Schweißfachmann");
    }

    /// <summary>
    /// <strong>"No text to read" is something other than "nothing found".</strong>
    /// </summary>
    /// <remarks>
    /// ADR-0022 §3, verbatim: whoever has nothing on GitHub is not worse but
    /// elsewhere — and a view that does not say so lies by omission. A
    /// photographed master craftsman's certificate is a picture. Showing both as
    /// an empty word list would tell its owner silently that nothing stands in
    /// it.
    /// </remarks>
    [Fact]
    public async Task A_picture_says_there_was_no_text_to_read()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        await Upload(hers, APicture, "certificate.png", "Master certificate");
        await Upload(hers, APdfWith("Ein Zeugnis ohne bekannte Woerter."), "r.pdf", "Reference");

        var state = await Json(await hers.PostAsync("/resumes/me/documents/read", null));

        var picture = state.EnumerateArray()
            .First(entry => entry.GetProperty("name").GetString() == "Master certificate");
        var text = state.EnumerateArray()
            .First(entry => entry.GetProperty("name").GetString() == "Reference");

        // Neither has words …
        picture.GetProperty("terms").GetArrayLength().Should().Be(0);
        text.GetProperty("terms").GetArrayLength().Should().Be(0);

        // … and they still differ. That is exactly the point.
        picture.GetProperty("has_text").GetBoolean().Should().BeFalse();
        text.GetProperty("has_text").GetBoolean().Should().BeTrue();

        // And both are READ — "never read" is the third state.
        picture.GetProperty("read_at").ValueKind.Should().NotBe(JsonValueKind.Null);
        text.GetProperty("read_at").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>Reading twice does not create a second row.</summary>
    /// <remarks>
    /// The button reads everything anew every time — on purpose, because the
    /// vocabulary grows by pull request, and a reference read before would
    /// otherwise carry its terms incomplete for ever. It replaces while doing
    /// so, instead of piling up: otherwise the same suggestion would stand in
    /// the list twice.
    /// </remarks>
    [Fact]
    public async Task Reading_twice_replaces_instead_of_piling_up()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);

        await Upload(hers, APdfWith("Kenntnisse in CNC und SPS."), "r.pdf", "Reference");

        await hers.PostAsync("/resumes/me/documents/read", null);
        var second = await Json(await hers.PostAsync("/resumes/me/documents/read", null));

        second.GetArrayLength().Should().Be(1);
        TermsOf(second, "Reference").Should().Equal("CNC", "SPS");
        (await TermRowsFor(anna)).Should().Be(1);
        _counter.Calls.Should().Be(2);
    }

    /// <summary>Whoever takes the document away takes its terms with it.</summary>
    /// <remarks>
    /// If they stayed, the profile page would keep offering words from a
    /// reference the person has just deleted — evidence without its subject.
    /// </remarks>
    [Fact]
    public async Task With_the_document_its_terms_fall()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);

        var created = await Upload(hers, APdfWith("Kenntnisse in CNC."), "r.pdf", "Reference");
        var id = (await Json(created)).GetProperty("id").GetGuid();

        await hers.PostAsync("/resumes/me/documents/read", null);
        (await TermRowsFor(anna)).Should().Be(1);

        (await hers.DeleteAsync($"/resumes/me/documents/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await TermRowsFor(anna)).Should().Be(0);
        (await Json(await hers.GetAsync("/resumes/me/documents/terms"))).GetArrayLength().Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // THE INDEX FALLS WITH THE ERASURE (ADR-0027)
    // -----------------------------------------------------------------------

    /// <summary>
    /// <strong>The erasure takes the index with it.</strong>
    /// </summary>
    /// <remarks>
    /// The condition under which this index was allowed to be built at all —
    /// and the place where it breaks most quietly: after an erasure the
    /// documents are gone too, so <c>/terms</c> answers empty anyway. A test that
    /// looks only at the answer would be green while the table still held the
    /// words from the references of a deleted human being. It is therefore
    /// counted in the DATABASE.
    /// </remarks>
    [Fact]
    public async Task Erasing_the_person_takes_the_index_with_it()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);

        await Upload(hers, APdfWith("Schweissfachmann DVS, mit Urkunde."), "a.pdf", "One");
        await Upload(hers, APdfWith("Kenntnisse in CNC und SPS."), "b.pdf", "Two");
        await hers.PostAsync("/resumes/me/documents/read", null);

        (await TermRowsFor(anna)).Should().Be(2);

        var eraser = _service.CreateClient();
        eraser.DefaultRequestHeaders.Add("X-Erasure-Secret", "erasure-secret");

        (await eraser.PostAsJsonAsync("/erasure", new { user_id = anna })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await TermRowsFor(anna)).Should().Be(0);
    }

    /// <summary>An account without documents gets an empty answer, not an error.</summary>
    /// <remarks>
    /// The promise "nothing gets worse": whoever never uploaded anything sees
    /// the same page as before. The button then does nothing either — and says
    /// so, instead of failing.
    /// </remarks>
    [Fact]
    public async Task Without_documents_the_answer_is_empty()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        (await Json(await hers.GetAsync("/resumes/me/documents/terms")))
            .GetArrayLength().Should().Be(0);

        var read = await hers.PostAsync("/resumes/me/documents/read", null);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(read)).GetArrayLength().Should().Be(0);
        _counter.Calls.Should().Be(0);
    }

    /// <summary>Without a token neither of the two addresses answers anything.</summary>
    [Fact]
    public async Task Without_signing_in_there_is_nothing()
    {
        var stranger = _service.CreateClient();

        (await stranger.GetAsync("/resumes/me/documents/terms")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await stranger.PostAsync("/resumes/me/documents/read", null)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Only what belongs to me is read.</summary>
    /// <remarks>
    /// The button takes no id; whose documents are read comes from the verified
    /// token. An id in the path would be the invitation to change it.
    /// </remarks>
    [Fact]
    public async Task The_button_reads_only_ones_own_documents()
    {
        var anna = Guid.CreateVersion7();
        var bodo = Guid.CreateVersion7();

        await Upload(AsPerson(anna), APdfWith("Kenntnisse in CNC."), "r.pdf", "Anna's reference");

        var his = await Json(await AsPerson(bodo).PostAsync("/resumes/me/documents/read", null));

        his.GetArrayLength().Should().Be(0);
        _counter.Calls.Should().Be(0);
        (await TermRowsFor(anna)).Should().Be(0);
    }
}
