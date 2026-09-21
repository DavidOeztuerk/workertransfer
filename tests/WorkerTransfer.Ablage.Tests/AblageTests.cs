using FluentAssertions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Ablage;

namespace WorkerTransfer.Ablage.Tests;

/// <summary>The document store from ADR-0021 — port, backend, signature check.</summary>
public sealed class AblageTests : IDisposable
{
    private readonly string _verzeichnis =
        Path.Combine(Path.GetTempPath(), "wt-ablage-" + Guid.CreateVersion7().ToString("N"));

    private LocalDocumentStore Build() =>
        new(Options.Create(new DocumentStoreSettings { Verzeichnis = _verzeichnis }));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_verzeichnis))
        {
            Directory.Delete(_verzeichnis, recursive: true);
        }
    }

    [Fact]
    public async Task What_was_stored_comes_back_unchanged()
    {
        var ablage = Build();
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

        await ablage.PutAsync("person/abc.png", content);

        (await ablage.GetAsync("person/abc.png")).Should().Equal(content);
    }

    /// <summary>"Does not exist" is an outcome of a fetch, not an error.</summary>
    /// <remarks>
    /// An exception would force every caller into a <c>try</c> around the normal
    /// case — and on a fetch the normal case is precisely that nothing is there.
    /// </remarks>
    [Fact]
    public async Task What_does_not_exist_comes_back_as_null()
    {
        (await Build().GetAsync("nichts/da.png")).Should().BeNull();
    }

    /// <summary>Deleting stays silent about an unknown key.</summary>
    [Fact]
    public async Task Deleting_stays_silent_about_the_unknown()
    {
        var attempt = async () => await Build().DeleteAsync("nie/dagewesen.png");

        await attempt.Should().NotThrowAsync();
    }

    [Fact]
    public async Task What_was_deleted_is_gone()
    {
        var ablage = Build();
        await ablage.PutAsync("weg/damit.png", new byte[] { 1, 2, 3 });

        await ablage.DeleteAsync("weg/damit.png");

        (await ablage.GetAsync("weg/damit.png")).Should().BeNull();
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
    [InlineData("../ausbruch.png")]
    [InlineData("a/../../ausbruch.png")]
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
    /// <c>zeugnis.png</c>.
    /// </remarks>
    [Fact]
    public void A_forged_extension_does_not_help()
    {
        // MZ — the beginning of a Windows executable, here as "zeugnis.png".
        Typerkennung.Erkenne([0x4D, 0x5A, 0x90, 0x00]).Should().BeNull();

        // And SVG is no image in this sense: it is a document with scripts.
        Typerkennung.Erkenne("<svg xmlns=\"http://www.w3.org/2000/svg\">"u8).Should().BeNull();
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    public void The_three_allowed_types_are_recognised(byte[] anfang, string erwartet)
    {
        Typerkennung.Erkenne(anfang).Should().Be(erwartet);
        Typerkennung.Erlaubt.Should().Contain(erwartet);
    }

    /// <summary>A beginning that is too short is not a hit but nothing.</summary>
    [Fact]
    public void Too_few_bytes_are_not_a_type()
    {
        Typerkennung.Erkenne([0x89, 0x50]).Should().BeNull();
        Typerkennung.Erkenne([]).Should().BeNull();
    }
}
