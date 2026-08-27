using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Was <see cref="ProblemDetailsMiddleware"/> aus einer geworfenen Ausnahme macht.
/// </summary>
/// <remarks>
/// Hier und nicht in einem Dienst, weil die Zwischenschicht in
/// <c>src/shared</c> liegt und für alle elf gilt: eine Gestalt über alle
/// Dienste, damit ein Aufrufer nicht wissen muss, wer geantwortet hat, um den
/// Fehler zu lesen.
/// <para>
/// Beide Zweige hier wurden im Betrieb gefunden, nicht im Entwurf: ein
/// abgeschnittener JSON-Rumpf kam an <em>jedem</em> Endpunkt als 500 zurück,
/// und Girders <c>ValidationBehavior</c> wirft eine
/// <c>ValidationException</c>, die ohne eigenen Zweig ebenfalls 500 geworden
/// wäre — beim ersten Validator, den jemand schreibt.
/// </para>
/// </remarks>
public sealed class FehlergestaltTests
{
    private static WebApplication Wirt(Func<Task> was)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();

        var wirt = bau.Build();
        wirt.UseMiddleware<ProblemDetailsMiddleware>();
        wirt.Map("/werfen", async () => await was());

        return wirt;
    }

    private static async Task<(HttpStatusCode Code, JsonElement Rumpf, string? Typ)> Frag(
        WebApplication wirt, HttpRequestMessage anfrage)
    {
        using var browser = new HttpClient { BaseAddress = new Uri(wirt.Urls.First()) };
        using var antwort = await browser.SendAsync(anfrage);
        var roh = await antwort.Content.ReadAsStringAsync();

        return (antwort.StatusCode,
                JsonDocument.Parse(roh).RootElement,
                antwort.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Ein unlesbarer Rumpf ist ein Fehler des Aufrufers, kein Serverfehler.
    /// </summary>
    /// <remarks>
    /// Die Ausnahme wird hier GEWORFEN statt durch einen abgeschnittenen Rumpf
    /// ausgelöst: in einem nackten Minimal-Wirt fängt das Rahmenwerk den
    /// Bindungsfehler selbst ab und antwortet mit leerem 400, bevor eine eigene
    /// Zwischenschicht ihn sieht. Im echten Dienst kommt er an — dort gemessen,
    /// an drei Diensten, jeweils 400 mit diesem Dokument. Was hier geprüft
    /// wird, ist der Zweig, nicht der Weg dorthin.
    /// </remarks>
    [Fact]
    public async Task Ein_kaputter_Rumpf_gibt_400()
    {
        await using var wirt = Wirt(() => throw new BadHttpRequestException(
            "Failed to read parameter \"ProfilKoerper koerper\" from the request body as JSON.",
            StatusCodes.Status400BadRequest));
        await wirt.StartAsync();

        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/werfen");
        var (code, rumpf, typ) = await Frag(wirt, anfrage);

        code.Should().Be(HttpStatusCode.BadRequest);
        rumpf.GetProperty("detail").GetString().Should().Be("malformed request body");
        typ.Should().Be("application/problem+json");
    }

    /// <summary>
    /// Der abgeschnittene Rumpf steht nicht in der Antwort.
    /// </summary>
    /// <remarks>
    /// Die Meldung von <c>BadHttpRequestException</c> nennt den Parameter und
    /// die Byteposition — das ist schon eine Aussage über das, was jemand
    /// geschickt hat. Sie wird deshalb nicht durchgereicht.
    /// </remarks>
    [Fact]
    public async Task Der_kaputte_Rumpf_taucht_in_der_Antwort_nicht_auf()
    {
        await using var wirt = Wirt(() => throw new BadHttpRequestException(
            "Failed to read parameter \"Wert\": Termin bei Dr. Weber",
            StatusCodes.Status400BadRequest));
        await wirt.StartAsync();

        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/werfen");
        var (_, rumpf, _) = await Frag(wirt, anfrage);

        rumpf.ToString().Should().NotContain("Weber");
        rumpf.ToString().Should().NotContain("Wert");
    }

    /// <summary>Eine gescheiterte Prüfung gibt 422 und nennt die Felder.</summary>
    [Fact]
    public async Task Eine_gescheiterte_Pruefung_gibt_422_mit_Feldnamen()
    {
        var fehler = new[]
        {
            new FluentValidation.Results.ValidationFailure("Email", "email is required"),
            new FluentValidation.Results.ValidationFailure("Passwort", "password is required")
        };

        await using var wirt = Wirt(() => throw new ValidationException(fehler));
        await wirt.StartAsync();

        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/werfen");
        var (code, rumpf, typ) = await Frag(wirt, anfrage);

        code.Should().Be(HttpStatusCode.UnprocessableEntity);
        rumpf.GetProperty("detail").GetString().Should().Be("invalid: Email, Passwort");
        typ.Should().Be("application/problem+json");
    }

    /// <summary>
    /// Die Antwort nennt Feldnamen, nie Werte.
    /// </summary>
    /// <remarks>
    /// FluentValidations Vorgabemeldungen setzen Platzhalter wie
    /// <c>{PropertyValue}</c> ein. Würde die Zwischenschicht
    /// <c>ErrorMessage</c> durchreichen, stünde der Wert einer Person in einem
    /// Fehlerdokument — und damit in Bildschirmfotos und Fehlerberichten.
    /// </remarks>
    [Fact]
    public async Task Die_Pruefantwort_traegt_keinen_Wert()
    {
        var fehler = new[]
        {
            new FluentValidation.Results.ValidationFailure(
                "Email", "'Email' hat den Wert 'anna@example.org' und ist ungueltig")
        };

        await using var wirt = Wirt(() => throw new ValidationException(fehler));
        await wirt.StartAsync();

        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/werfen");
        var (_, rumpf, _) = await Frag(wirt, anfrage);

        rumpf.ToString().Should().NotContain("anna");
        rumpf.ToString().Should().NotContain("example.org");
        rumpf.GetProperty("detail").GetString().Should().Be("invalid: Email");
    }

    /// <summary>Alles andere bleibt 500 — ein Serverfehler ist einer.</summary>
    [Fact]
    public async Task Eine_unerwartete_Ausnahme_bleibt_500()
    {
        await using var wirt = Wirt(() => throw new InvalidOperationException("kaputt"));
        await wirt.StartAsync();

        using var anfrage = new HttpRequestMessage(HttpMethod.Get, "/werfen");
        var (code, rumpf, _) = await Frag(wirt, anfrage);

        code.Should().Be(HttpStatusCode.InternalServerError);
        rumpf.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        rumpf.ToString().Should().NotContain("kaputt", "die Ausnahmemeldung gehoert ins Log, nicht in die Antwort");
    }
}
