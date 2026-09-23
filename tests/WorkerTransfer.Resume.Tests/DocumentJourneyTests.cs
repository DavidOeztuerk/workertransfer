using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Resume.Tests;

/// <summary>Documents: upload, show, release, delete — and the file with them.</summary>
/// <remarks>
/// <para><strong>With a real document store on disk, not with a
/// probe.</strong> Half the promises of ADR-0035 concern the <em>file</em> and
/// not the row: that a forged extension does not help, that a deletion clears
/// the bytes too, that a row without a file looks from outside like "does not
/// exist". An in-memory store probe would confirm all three without checking
/// any of them.</para>
///
/// <para>The directory is per suite and cleared afterwards — two suites that
/// share a document store are two suites whose order matters.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class DocumentJourneyTests(Postgres postgres) : IAsyncLifetime
{
    /// <summary>A real PNG — eight bytes of signature suffice for the detection.</summary>
    private static readonly byte[] APng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    /// <summary>A real PDF.</summary>
    private static readonly byte[] APdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    /// <summary>The start of a Windows executable. Not a picture, whatever it is called.</summary>
    private static readonly byte[] AProgram = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];

    private readonly string _documentStore = Path.Combine(
        Path.GetTempPath(), "wt-documents-" + Guid.CreateVersion7().ToString("N"));

    private readonly ProbeGate _gate = new();
    private readonly ProbeDelivery _delivery = new();
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
            host.UseSetting("Erasure:Geheimnis", "erasure-secret");
            host.UseSetting("Ablage:Verzeichnis", _documentStore);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _gate));
                services.Replace(ServiceDescriptor.Scoped<IDelivery>(_ => _delivery));
            });
        });

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

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient AsCompany(Guid subject, Guid company) =>
        With(TokenShape.Company(subject, company));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Uploads the way a browser would: multipart, with a name and a kind.</summary>
    /// <remarks>
    /// <paramref name="kind"/> is a wire word (<c>zeugnis</c>, <c>sonstiges</c>,
    /// …) — the web app sends exactly these.
    /// </remarks>
    private static Task<HttpResponseMessage> Upload(
        HttpClient browser, byte[] content, string fileName, string name, string kind = "zeugnis")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);

        // The CLAIMED type, on purpose: the service must not believe it.
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(kind), "kind");

        return browser.PostAsync("/resumes/me/documents", form);
    }

    /// <summary>How many files really lie on disk.</summary>
    private int FilesOnDisk() =>
        Directory.Exists(_documentStore)
            ? Directory.GetFiles(_documentStore, "*", SearchOption.AllDirectories).Length
            : 0;

    /// <summary>The whole way — and the file really lies there meanwhile.</summary>
    [Fact]
    public async Task Upload_show_fetch_delete()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        var created = await Upload(hers, APdf, "reference.pdf", "Employment reference Acme");

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var entry = await Json(created);
        var id = entry.GetProperty("id").GetGuid();

        entry.GetProperty("name").GetString().Should().Be("Employment reference Acme");
        entry.GetProperty("kind").GetString().Should().Be("zeugnis");
        // The type comes from the BYTES: uploaded claiming "image/png".
        entry.GetProperty("content_type").GetString().Should().Be("application/pdf");
        entry.GetProperty("size_bytes").GetInt32().Should().Be(APdf.Length);

        FilesOnDisk().Should().Be(1, "the bytes lie in the document store, not in the row");

        var list = await Json(await hers.GetAsync("/resumes/me/documents"));
        list.GetArrayLength().Should().Be(1);

        var content = await hers.GetAsync($"/resumes/me/documents/{id}/content");
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(APdf);

        (await hers.DeleteAsync($"/resumes/me/documents/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        // ROW AND FILE. Deleting rows and leaving bytes lying around would be a
        // broken promise nobody notices: the interface looks empty afterwards,
        // the disk does not (ADR-0027, ADR-0035).
        (await Json(await hers.GetAsync("/resumes/me/documents"))).GetArrayLength().Should().Be(0);
        FilesOnDisk().Should().Be(0);
    }

    /// <summary>
    /// <strong>A forged extension does not help.</strong>
    /// </summary>
    /// <remarks>
    /// The file name and the <c>Content-Type</c> are both freely chosen; the
    /// signature is not. Whoever believes the claim accepts an executable
    /// because somebody named it <c>reference.png</c>.
    /// </remarks>
    [Fact]
    public async Task A_forged_extension_does_not_help()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        var rejected = await Upload(hers, AProgram, "reference.png", "Looks like a picture");

        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // And nothing stays behind either: first the bytes, then the row — if
        // it is refused, neither is written.
        FilesOnDisk().Should().Be(0);
        (await Json(await hers.GetAsync("/resumes/me/documents"))).GetArrayLength().Should().Be(0);
    }

    /// <summary>And the other way round: a real PNG with a wrong extension goes through.</summary>
    /// <remarks>
    /// The counter-half. Without it the finding above might mean "nothing goes
    /// through at all".
    /// </remarks>
    [Fact]
    public async Task A_real_picture_with_a_wrong_extension_goes_through()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        var created = await Upload(hers, APng, "reference.txt", "A picture after all");

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(created)).GetProperty("content_type").GetString().Should().Be("image/png");
    }

    /// <summary>Ten are enough.</summary>
    /// <remarks>
    /// Without a limit a caller builds an arbitrarily expensive document store
    /// with a loop — and an application with thirty attachments nobody reads
    /// anyway.
    /// </remarks>
    [Fact]
    public async Task Nobody_accepts_more_than_ten()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        for (var number = 1; number <= 10; number++)
        {
            (await Upload(hers, APdf, $"r{number}.pdf", $"Reference {number}")).StatusCode
                .Should().Be(HttpStatusCode.Created);
        }

        (await Upload(hers, APdf, "r11.pdf", "One too many")).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);

        FilesOnDisk().Should().Be(10);
    }

    /// <summary>A stranger does not see them — and does not even notice they exist.</summary>
    [Fact]
    public async Task A_stranger_does_not_see_ones_own_documents()
    {
        var anna = Guid.CreateVersion7();
        await Upload(AsPerson(anna), APdf, "r.pdf", "Anna's reference");
        var stranger = AsPerson(Guid.CreateVersion7());

        // `/me` is per caller: the stranger gets THEIR list, not Anna's.
        (await Json(await stranger.GetAsync("/resumes/me/documents")))
            .GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// <strong>The company sees nothing as long as nothing is released.</strong>
    /// </summary>
    /// <remarks>
    /// And the empty list is the same one somebody without documents gets. A
    /// difference would betray that this person <em>has</em> some, and that is
    /// a fact about them (ADR-0020).
    /// </remarks>
    [Fact]
    public async Task Without_a_release_the_company_sees_nothing()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();

        var created = await Json(await Upload(AsPerson(anna), APdf, "r.pdf", "Reference"));
        var id = created.GetProperty("id").GetGuid();

        var manager = AsCompany(Guid.CreateVersion7(), company);

        (await Json(await manager.GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(0);

        (await manager.GetAsync($"/resumes/{anna}/documents/{id}/content")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        // Somebody without documents looks exactly the same from outside.
        (await Json(await manager.GetAsync($"/resumes/{Guid.CreateVersion7()}/documents")))
            .GetArrayLength().Should().Be(0);
    }

    /// <summary>With a release it sees them — and the ledger is asked every time.</summary>
    /// <remarks>
    /// A revocation must take effect on the next call, not on the one after
    /// (ADR-0013). That is why this test checks both directions in one go:
    /// grant, read, revoke, read again.
    /// </remarks>
    [Fact]
    public async Task With_a_release_the_company_sees_them_and_a_revocation_takes_effect_at_once()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();

        var created = await Json(await Upload(AsPerson(anna), APdf, "r.pdf", "Reference"));
        var id = created.GetProperty("id").GetGuid();

        _gate.DocumentsReleased.Add((anna, company));

        var manager = AsCompany(Guid.CreateVersion7(), company);

        (await Json(await manager.GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(1);

        var content = await manager.GetAsync($"/resumes/{anna}/documents/{id}/content");
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(APdf);

        _gate.DocumentsReleased.Remove((anna, company));

        (await Json(await manager.GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(0);
        (await manager.GetAsync($"/resumes/{anna}/documents/{id}/content")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// <strong>The release of the résumé is NOT that of the documents.</strong>
    /// </summary>
    /// <remarks>
    /// Two capabilities, two decisions: a career history is self-written text,
    /// a reference a third party's document with names and grades (ADR-0035).
    /// Whoever wants to show the one and not the other must be able to — and if
    /// one release opened both, they could not.
    /// </remarks>
    [Fact]
    public async Task The_resume_release_does_not_open_the_documents()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();

        await Upload(AsPerson(anna), APdf, "r.pdf", "Reference");

        // ONLY the résumé is released.
        _gate.ResumeReleased.Add((anna, company));

        (await Json(await AsCompany(Guid.CreateVersion7(), company)
                .GetAsync($"/resumes/{anna}/documents")))
            .GetArrayLength().Should().Be(0);
    }

    /// <summary>A private person reads no foreign documents.</summary>
    /// <remarks>
    /// 403 and not 404: "no active company" is a statement about the
    /// <em>caller</em> and betrays nothing about the other human being.
    /// </remarks>
    [Fact]
    public async Task A_private_person_reads_no_foreign_documents()
    {
        var anna = Guid.CreateVersion7();
        await Upload(AsPerson(anna), APdf, "r.pdf", "Reference");

        (await AsPerson(Guid.CreateVersion7()).GetAsync($"/resumes/{anna}/documents"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>An uploaded file can be THE résumé.</summary>
    /// <remarks>
    /// The second way to the résumé: whoever has it as a PDF should not have to
    /// type it out. The kind changes, the file stays the same.
    /// </remarks>
    [Fact]
    public async Task A_file_becomes_the_resume()
    {
        var hers = AsPerson(Guid.CreateVersion7());
        var id = (await Json(await Upload(hers, APdf, "cv.pdf", "My CV", "sonstiges")))
            .GetProperty("id").GetGuid();

        (await hers.PutAsync($"/resumes/me/documents/{id}/as-cv", content: null)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var list = await Json(await hers.GetAsync("/resumes/me/documents"));
        list[0].GetProperty("kind").GetString().Should().Be("lebenslauf");
    }

    /// <summary>What does not exist does not become the résumé.</summary>
    [Fact]
    public async Task A_foreign_id_does_not_become_the_resume()
    {
        (await AsPerson(Guid.CreateVersion7())
                .PutAsync($"/resumes/me/documents/{Guid.CreateVersion7()}/as-cv", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The template is a value, and it comes back.</summary>
    [Fact]
    public async Task A_template_is_set_and_read()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        await hers.PutAsJsonAsync("/resumes/me", new
        {
            positions = Array.Empty<object>(),
            education = Array.Empty<object>()
        });

        (await hers.PutAsJsonAsync("/resumes/me/template", new { template = "modern" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Json(await hers.GetAsync("/resumes/me")))
            .GetProperty("template").GetString().Should().Be("modern");
    }

    /// <summary>
    /// <strong>An unknown template is refused, not quietly turned.</strong>
    /// </summary>
    /// <remarks>
    /// Otherwise somebody chooses something, gets 204 back and sees something
    /// else afterwards — an error that looks like the software changing its
    /// mind.
    /// </remarks>
    [Fact]
    public async Task An_unknown_template_is_refused()
    {
        var hers = AsPerson(Guid.CreateVersion7());

        await hers.PutAsJsonAsync("/resumes/me", new
        {
            positions = Array.Empty<object>(),
            education = Array.Empty<object>()
        });

        (await hers.PutAsJsonAsync("/resumes/me/template", new { template = "glitter" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// <strong>The erasure clears rows AND files.</strong>
    /// </summary>
    /// <remarks>
    /// The promise of ADR-0027, at the place where it breaks most quietly: the
    /// interface looks empty after an erasure whether the bytes still lie there
    /// or not. Only a look at the disk tells the two apart.
    /// </remarks>
    [Fact]
    public async Task Erasing_the_person_also_clears_the_document_store()
    {
        var anna = Guid.CreateVersion7();
        var hers = AsPerson(anna);

        await Upload(hers, APdf, "a.pdf", "One");
        await Upload(hers, APng, "b.png", "Two");

        FilesOnDisk().Should().Be(2);

        var eraser = _service.CreateClient();
        eraser.DefaultRequestHeaders.Add("X-Erasure-Secret", "erasure-secret");

        (await eraser.PostAsJsonAsync("/erasure", new { user_id = anna })).StatusCode
            .Should().Be(HttpStatusCode.OK);

        (await Json(await hers.GetAsync("/resumes/me/documents"))).GetArrayLength().Should().Be(0);
        FilesOnDisk().Should().Be(0);
    }
}
