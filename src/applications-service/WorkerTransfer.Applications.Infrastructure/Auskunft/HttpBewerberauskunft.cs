using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Infrastructure.Auskunft;

/// <summary>Wo die Dienste antworten, die die eigenen Angaben halten.</summary>
public sealed class Auskunftseinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Auskunft";

    /// <summary>identity-service — Name und Sprache.</summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>profile-service — Überschrift, Text, Fähigkeiten.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>resume-service — der Werdegang.</summary>
    public string Resume { get; set; } = string.Empty;

    /// <summary>companies-service — der Name des Unternehmens.</summary>
    public string Companies { get; set; } = string.Empty;

    /// <summary>Wie lange ein Aufruf dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

internal sealed record Sitzungsantwort(
    [property: JsonPropertyName("user")] Sitzungsbenutzer? User);

internal sealed record Sitzungsbenutzer(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("given_name")] string? GivenName,
    [property: JsonPropertyName("family_name")] string? FamilyName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("language")] string? Language);

internal sealed record Anschriftantwort(
    [property: JsonPropertyName("line1")] string? Line1,
    [property: JsonPropertyName("line2")] string? Line2,
    [property: JsonPropertyName("postal_code")] string? PostalCode,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("phone")] string? Phone);

internal sealed record Profilantwort(
    [property: JsonPropertyName("headline")] string? Headline,
    [property: JsonPropertyName("bio")] string? Bio,
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills);

internal sealed record Stationsantwort(
    [property: JsonPropertyName("employer")] string? Employer,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("started_on")] string? StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies);

internal sealed record Lebenslaufantwort(
    [property: JsonPropertyName("positions")] IReadOnlyList<Stationsantwort>? Positions);

/// <summary>
/// Öffentliches Arbeitgeberprofil — dieselben Namen wie
/// <c>ArbeitgeberprofilV1</c>. <c>name</c> statt <c>display_name</c> kam
/// immer leer an, und der Brief ging ohne Firma hinaus.
/// </summary>
public sealed record Firmenprofilantwort(
    [property: JsonPropertyName("display_name")] string? Name,
    [property: JsonPropertyName("line1")] string? Line1 = null,
    [property: JsonPropertyName("postal_code")] string? PostalCode = null,
    [property: JsonPropertyName("city")] string? City = null,
    [property: JsonPropertyName("country")] string? Country = null);

/// <summary>Holt die eigenen Angaben — mit dem Token des Aufrufers.</summary>
/// <remarks>
/// <strong>Der Kontext wird serverseitig gefüllt, und das ist der Punkt.</strong>
/// Ihn vom Browser schicken zu lassen wäre kürzer und machte die Zusage aus
/// ADR-0034 wertlos: „nur die eigenen Daten" ist eine Behauptung, die nur der
/// Server halten kann. Geholt wird mit dem Token des Aufrufers — also genau
/// das, was diese Person ohnehin sehen darf, und keine Zeile mehr.
/// <para>
/// <strong>Ein fehlender Teil ist kein Fehler.</strong> Wer kein Profil und
/// keinen Lebenslauf hinterlegt hat, bekommt ein dünneres Anschreiben, keine
/// Fehlermeldung. Nur wenn ein Dienst <em>antwortet, aber unbrauchbar</em>,
/// oder gar nicht antwortet, fliegt <see cref="BewerberSchweigt" /> — denn dann
/// wüssten wir nicht, ob etwas fehlt oder nur gerade nicht zu bekommen ist.
/// </para>
/// </remarks>
public sealed class HttpBewerberauskunft(
    IHttpClientFactory fabrik,
    IOptions<Auskunftseinstellungen> einstellungen,
    IAufrufertoken token) : IBewerberauskunft, IUnternehmensauskunft, IKontaktauskunft
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "auskunft";

    private readonly Auskunftseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Eigenbild> HoleAsync(CancellationToken cancellationToken = default)
    {
        var sitzung = await LiesAsync<Sitzungsantwort>(
            _einstellungen.Identity, "/auth/session", cancellationToken);
        var profil = await LiesAsync<Profilantwort>(
            _einstellungen.Profile, "/profiles/me", cancellationToken);
        var lebenslauf = await LiesAsync<Lebenslaufantwort>(
            _einstellungen.Resume, "/resumes/me", cancellationToken);

        return new Eigenbild(
            Klarname(sitzung?.User),
            profil?.Headline ?? string.Empty,
            profil?.Bio ?? string.Empty,
            profil?.Skills ?? [],
            [
                .. (lebenslauf?.Positions ?? []).Select(station => new Werdegangstation(
                    station.Employer ?? string.Empty,
                    station.Title ?? string.Empty,
                    // „seit" statt eines offenen Bindestrichs: eine laufende
                    // Stelle ist eine Aussage, kein fehlender Wert.
                    station.EndedOn is { Length: > 0 } ende
                        ? $"{station.StartedOn} bis {ende}"
                        : $"seit {station.StartedOn}",
                    station.Description ?? string.Empty,
                    station.Technologies ?? []))
            ],
            sitzung?.User?.Language ?? "de");
    }

    /// <inheritdoc />
    public async Task<string> NameAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var profil = await LiesAsync<Firmenprofilantwort>(
            _einstellungen.Companies,
            $"/companies/{firma.Value}/profile",
            cancellationToken);

        return profil?.Name ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<Bewerbungskontakt> HoleKontaktAsync(CancellationToken cancellationToken = default)
    {
        // Geteilter Name mit IBewerberauskunft — deshalb explizit. Anschrift
        // darf in Eigenbild NICHT landen; dieser Weg ist nur der Snapshot.
        try
        {
            var sitzung = await LiesAsync<Sitzungsantwort>(
                _einstellungen.Identity, "/auth/session", cancellationToken);
            var anschrift = await LiesAsync<Anschriftantwort>(
                _einstellungen.Identity, "/account/address", cancellationToken);

            return new Bewerbungskontakt(
                Klarname(sitzung?.User),
                anschrift?.Line1 ?? string.Empty,
                anschrift?.Line2 ?? string.Empty,
                anschrift?.PostalCode ?? string.Empty,
                anschrift?.City ?? string.Empty,
                string.IsNullOrEmpty(anschrift?.Country) ? "DE" : anschrift.Country,
                anschrift?.Phone ?? string.Empty,
                sitzung?.User?.Email ?? string.Empty);
        }
        catch (BewerberSchweigt)
        {
            return Bewerbungskontakt.Leer;
        }
    }

    /// <summary>
    /// Vor- und Nachname, sonst der Anzeigename. Nie eine Anschrift.
    /// </summary>
    internal static string Klarname(Sitzungsbenutzer? wer)
    {
        var voll = $"{wer?.GivenName} {wer?.FamilyName}".Trim();
        return voll.Length > 0 ? voll : wer?.DisplayName ?? string.Empty;
    }

    /// <summary>Ein GET mit dem Token des Aufrufers. <c>null</c> heißt „nichts da".</summary>
    private async Task<T?> LiesAsync<T>(
        string basis, string pfad, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(basis))
        {
            // Nicht eingerichtet heisst: dieser Teil fehlt. Das ist eine
            // Konfigurationslage und kein Ausfall — der Brief wird duenner.
            return null;
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(basis), pfad));

        if (token.Wert is { Length: > 0 } bearer)
        {
            anfrage.Headers.Add("Authorization", $"Bearer {bearer}");
        }

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            throw new BewerberSchweigt(
                $"Ein Dienst mit den eigenen Angaben antwortet nicht ({fehler.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BewerberSchweigt(
                "Ein Dienst mit den eigenen Angaben antwortet nicht (Zeitüberschreitung).");
        }

        using (antwort)
        {
            // 404 heisst „hat keins" — das ist eine Antwort und kein Ausfall.
            if (antwort.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!antwort.IsSuccessStatusCode)
            {
                throw new BewerberSchweigt(
                    $"Ein Dienst mit den eigenen Angaben antwortet mit {(int)antwort.StatusCode}.");
            }

            try
            {
                return await antwort.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (JsonException fehler)
            {
                throw new BewerberSchweigt(
                    "Ein Dienst mit den eigenen Angaben sandte eine unbrauchbare Antwort.",
                    fehler);
            }
        }
    }
}
