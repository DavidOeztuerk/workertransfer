using FluentAssertions;
using Microsoft.Extensions.Options;
namespace WorkerTransfer.DocumentStore.Tests;

/// <summary>The document store from ADR-0021 — port, backend, signature check.</summary>
public sealed class DocumentStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "wt-document-store-" + Guid.CreateVersion7().ToString("N"));

    private LocalDocumentStore Build() =>
        new(Options.Create(new DocumentStoreSettings { Folder = _directory }));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task What_was_stored_comes_back_unchanged()
    {
        var store = Build();
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

        await store.PutAsync("person/abc.png", content);

        (await store.GetAsync("person/abc.png")).Should().Equal(content);
    }

    /// <summary>"Does not exist" is an outcome of a fetch, not an error.</summary>
    /// <remarks>
    /// An exception would force every caller into a <c>try</c> around the normal
    /// case — and on a fetch the normal case is precisely that nothing is there.
    /// </remarks>
    [Fact]
    public async Task What_does_not_exist_comes_back_as_null()
    {
        (await Build().GetAsync("nothing/here.png")).Should().BeNull();
    }

    /// <summary>Deleting stays silent about an unknown key.</summary>
    [Fact]
    public async Task Deleting_stays_silent_about_the_unknown()
    {
        var attempt = async () => await Build().DeleteAsync("never/was.png");

        await attempt.Should().NotThrowAsync();
    }

    [Fact]
    public async Task What_was_deleted_is_gone()
    {
        var store = Build();
        await store.PutAsync("away/with-it.png", new byte[] { 1, 2, 3 });

        await store.DeleteAsync("away/with-it.png");

        (await store.GetAsync("away/with-it.png")).Should().BeNull();
    }

    /// <summary>
    /// <strong>A key must not lead out of the root.</strong>
    /// </summary>
    /// <remarks>
    /// Without this check <c>../../etc/passwd</c> would be a read path through
    /// the whole file system. What is checked is the RESULT of composing it, not
    /// the text: a text check misses the next spelling.
    /// </remarks>
    [Theory]
    [InlineData("../escape.png")]
    [InlineData("a/../../escape.png")]
    public async Task A_key_does_not_escape(string key)
    {
        var attempt = async () => await Build().GetAsync(key);

        await attempt.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// <strong>The type comes from the bytes, never from the claim.</strong>
    /// </summary>
    /// <remarks>
    /// A <c>Content-Type</c> and a file extension are both freely chosen.
    /// Whoever believes them accepts an executable because somebody named it
    /// <c>certificate.png</c>.
    /// </remarks>
    [Fact]
    public void A_forged_extension_does_not_help()
    {
        // MZ — the beginning of a Windows executable, here as "certificate.png".
        ContentTypeDetection.Detect([0x4D, 0x5A, 0x90, 0x00]).Should().BeNull();

        // And SVG is no image in this sense: it is a document with scripts.
        ContentTypeDetection.Detect("<svg xmlns=\"http://www.w3.org/2000/svg\">"u8).Should().BeNull();
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    public void The_three_allowed_types_are_recognised(byte[] beginning, string expected)
    {
        ContentTypeDetection.Detect(beginning).Should().Be(expected);
        ContentTypeDetection.Allowed.Should().Contain(expected);
    }

    /// <summary>A beginning that is too short is not a hit but nothing.</summary>
    [Fact]
    public void Too_few_bytes_are_not_a_type()
    {
        ContentTypeDetection.Detect([0x89, 0x50]).Should().BeNull();
        ContentTypeDetection.Detect([]).Should().BeNull();
    }
}
