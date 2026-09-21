using FluentAssertions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Ablage;

namespace WorkerTransfer.Ablage.Tests;

/// <summary>Die Ablage aus ADR-0021 — Port, Backend, Signaturprüfung.</summary>
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
    public async Task Was_abgelegt_wurde_kommt_unveraendert_zurueck()
    {
        var ablage = Build();
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

        await ablage.PutAsync("person/abc.png", content);

        (await ablage.GetAsync("person/abc.png")).Should().Equal(content);
    }

    /// <summary>„Gibt es nicht" ist beim Abrufen ein Ausgang, kein Fehler.</summary>
    /// <remarks>
    /// Eine Ausnahme zwänge jeden Aufrufer zu einem <c>try</c> um den Normalfall
    /// — und der Normalfall ist beim Abrufen eben auch, dass nichts da ist.
    /// </remarks>
    [Fact]
    public async Task Was_es_nicht_gibt_kommt_als_null()
    {
        (await Build().GetAsync("nichts/da.png")).Should().BeNull();
    }

    /// <summary>Löschen schweigt über einen unbekannten Schlüssel.</summary>
    [Fact]
    public async Task Loeschen_schweigt_ueber_Unbekanntes()
    {
        var attempt = async () => await Build().DeleteAsync("nie/dagewesen.png");

        await attempt.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Geloeschtes_ist_fort()
    {
        var ablage = Build();
        await ablage.PutAsync("weg/damit.png", new byte[] { 1, 2, 3 });

        await ablage.DeleteAsync("weg/damit.png");

        (await ablage.GetAsync("weg/damit.png")).Should().BeNull();
    }

    /// <summary>
    /// <strong>Ein Schlüssel darf nicht aus der Wurzel führen.</strong>
    /// </summary>
    /// <remarks>
    /// Ohne diese Prüfung wäre <c>../../etc/passwd</c> ein Leseweg durch das
    /// ganze Dateisystem. Geprüft wird das ERGEBNIS des Zusammensetzens, nicht
    /// der Text: eine Textprüfung übersieht die nächste Schreibweise.
    /// </remarks>
    [Theory]
    [InlineData("../ausbruch.png")]
    [InlineData("a/../../ausbruch.png")]
    public async Task Ein_Schluessel_bricht_nicht_aus(string key)
    {
        var attempt = async () => await Build().GetAsync(key);

        await attempt.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// <strong>Der Typ kommt aus den Bytes, nie aus der Behauptung.</strong>
    /// </summary>
    /// <remarks>
    /// Ein <c>Content-Type</c> und eine Dateiendung sind beide frei wählbar.
    /// Wer ihnen glaubt, nimmt eine ausführbare Datei entgegen, weil jemand sie
    /// <c>zeugnis.png</c> genannt hat.
    /// </remarks>
    [Fact]
    public void Eine_gefaelschte_Endung_hilft_nicht()
    {
        // MZ — der Anfang einer Windows-Programmdatei, hier als „zeugnis.png".
        Typerkennung.Erkenne([0x4D, 0x5A, 0x90, 0x00]).Should().BeNull();

        // Und SVG ist kein Bild in diesem Sinn: es ist ein Dokument mit Skripten.
        Typerkennung.Erkenne("<svg xmlns=\"http://www.w3.org/2000/svg\">"u8).Should().BeNull();
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    public void Die_drei_erlaubten_Typen_werden_erkannt(byte[] anfang, string erwartet)
    {
        Typerkennung.Erkenne(anfang).Should().Be(erwartet);
        Typerkennung.Erlaubt.Should().Contain(erwartet);
    }

    /// <summary>Ein zu kurzer Anfang ist kein Hit, sondern nichts.</summary>
    [Fact]
    public void Zu_wenige_Bytes_sind_kein_Typ()
    {
        Typerkennung.Erkenne([0x89, 0x50]).Should().BeNull();
        Typerkennung.Erkenne([]).Should().BeNull();
    }
}
