using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Infrastructure.Auskunft;

/// <summary>Wo die Dienste antworten, die die eigenen Angaben halten.</summary>
public sealed class LookupSettings
{
    /// <summary>Der Section in der Konfiguration.</summary>
    public const string Section = "Auskunft";

    /// <summary>identity-service — Name und Sprache.</summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>profile-service — Überschrift, Text, Fähigkeiten.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>resume-service — the career history.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Auskunft__Resume</c>). It was
    /// renamed to <c>CurriculumVitae</c> once, on its own: compose kept setting
    /// <c>Auskunft__Resume</c>, the address stayed empty, and every suite stayed
    /// green because none of them reads compose. <c>ComposeKeyTests</c> does.
    /// </remarks>
    public string Resume { get; set; } = string.Empty;

    /// <summary>companies-service — der Name des Unternehmens.</summary>
    public string Companies { get; set; } = string.Empty;

    /// <summary>Wie lange ein Aufruf dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

internal sealed record SessionResponse(
    [property: JsonPropertyName("user")] SessionUser? User);

internal sealed record SessionUser(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("given_name")] string? GivenName,
    [property: JsonPropertyName("family_name")] string? FamilyName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("language")] string? Language);

internal sealed record PostalAddressResponse(
    [property: JsonPropertyName("line1")] string? Line1,
    [property: JsonPropertyName("line2")] string? Line2,
    [property: JsonPropertyName("postal_code")] string? PostalCode,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("phone")] string? Phone);

internal sealed record ProfileResponse(
    [property: JsonPropertyName("headline")] string? Headline,
    [property: JsonPropertyName("bio")] string? Bio,
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills);

internal sealed record StationResponse(
    [property: JsonPropertyName("employer")] string? Employer,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("started_on")] string? StartedOn,
    [property: JsonPropertyName("ended_on")] string? EndedOn,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies);

internal sealed record ResumeResponse(
    [property: JsonPropertyName("positions")] IReadOnlyList<StationResponse>? Positions);

/// <summary>
/// Öffentliches EmployerProfile — dieselben Namen wie
/// <c>EmployerProfileV1</c>. <c>name</c> statt <c>display_name</c> kam
/// immer leer an, und der Brief ging ohne Firma hinaus.
/// </summary>
public sealed record CompanyProfileResponse(
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
/// <strong>Ein fehlender Teil ist kein Fehler.</strong> Wer kein Profile und
/// keinen CurriculumVitae hinterlegt hat, bekommt ein dünneres Anschreiben, keine
/// Fehlermeldung. Nur wenn ein Dienst <em>antwortet, aber unbrauchbar</em>,
/// oder gar nicht antwortet, fliegt <see cref="BewerberSchweigt" /> — denn dann
/// wüssten wir nicht, ob etwas fehlt oder nur gerade nicht zu bekommen ist.
/// </para>
/// </remarks>
public sealed class HttpApplicantLookup(
    IHttpClientFactory fabrik,
    IOptions<LookupSettings> einstellungen,
    IAufrufertoken token) : IApplicantLookup, IUnternehmensauskunft, IKontaktauskunft
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "auskunft";

    private readonly LookupSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Eigenbild> GetAsync(CancellationToken cancellationToken = default)
    {
        var session = await LiesAsync<SessionResponse>(
            _einstellungen.Identity, "/auth/session", cancellationToken);
        var profile = await LiesAsync<ProfileResponse>(
            _einstellungen.Profile, "/profiles/me", cancellationToken);
        var resume = await LiesAsync<ResumeResponse>(
            _einstellungen.Resume, "/resumes/me", cancellationToken);

        return new Eigenbild(
            Klarname(session?.User),
            profile?.Headline ?? string.Empty,
            profile?.Bio ?? string.Empty,
            profile?.Skills ?? [],
            [
                .. (resume?.Positions ?? []).Select(station => new Werdegangstation(
                    station.Employer ?? string.Empty,
                    station.Title ?? string.Empty,
                    // „seit" statt eines offenen Bindestrichs: eine laufende
                    // Job ist eine Aussage, kein fehlender Wert.
                    station.EndedOn is { Length: > 0 } ende
                        ? $"{station.StartedOn} bis {ende}"
                        : $"seit {station.StartedOn}",
                    station.Description ?? string.Empty,
                    station.Technologies ?? []))
            ],
            session?.User?.Language ?? "de");
    }

    /// <inheritdoc />
    public async Task<string> NameAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var profile = await LiesAsync<CompanyProfileResponse>(
            _einstellungen.Companies,
            $"/companies/{company.Value}/profile",
            cancellationToken);

        return profile?.Name ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<ApplicantContact> GetContactAsync(CancellationToken cancellationToken = default)
    {
        // Geteilter Name mit IApplicantLookup — deshalb explizit. PostalAddress
        // darf in Eigenbild NICHT landen; dieser Weg ist nur der Snapshot.
        try
        {
            var session = await LiesAsync<SessionResponse>(
                _einstellungen.Identity, "/auth/session", cancellationToken);
            var anschrift = await LiesAsync<PostalAddressResponse>(
                _einstellungen.Identity, "/account/address", cancellationToken);

            return new ApplicantContact(
                Klarname(session?.User),
                anschrift?.Line1 ?? string.Empty,
                anschrift?.Line2 ?? string.Empty,
                anschrift?.PostalCode ?? string.Empty,
                anschrift?.City ?? string.Empty,
                string.IsNullOrEmpty(anschrift?.Country) ? "DE" : anschrift.Country,
                anschrift?.Phone ?? string.Empty,
                session?.User?.Email ?? string.Empty);
        }
        catch (ApplicantLookupSilent)
        {
            return ApplicantContact.Leer;
        }
    }

    /// <summary>
    /// Vor- und Nachname, sonst der Anzeigename. Nie eine PostalAddress.
    /// </summary>
    internal static string Klarname(SessionUser? subject)
    {
        var voll = $"{subject?.GivenName} {subject?.FamilyName}".Trim();
        return voll.Length > 0 ? voll : subject?.DisplayName ?? string.Empty;
    }

    /// <summary>Ein GET mit dem Token des Aufrufers. <c>null</c> heißt „nichts da".</summary>
    private async Task<T?> LiesAsync<T>(
        string basis, string path, CancellationToken cancellationToken)
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

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(basis), path));

        if (token.Wert is { Length: > 0 } bearer)
        {
            request.Headers.Add("Authorization", $"Bearer {bearer}");
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new ApplicantLookupSilent(
                $"Ein Dienst mit den eigenen Angaben antwortet nicht ({error.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApplicantLookupSilent(
                "Ein Dienst mit den eigenen Angaben antwortet nicht (Zeitüberschreitung).");
        }

        using (response)
        {
            // 404 heisst „hat keins" — das ist eine Antwort und kein Ausfall.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ApplicantLookupSilent(
                    $"Ein Dienst mit den eigenen Angaben antwortet mit {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (JsonException error)
            {
                throw new ApplicantLookupSilent(
                    "Ein Dienst mit den eigenen Angaben sandte eine unbrauchbare Antwort.",
                    error);
            }
        }
    }
}
