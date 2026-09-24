using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Infrastructure.Lookup;

/// <summary>Where the services answer that hold one's own details.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Auskunft__Identity</c>, <c>Auskunft__Profile</c>, <c>Auskunft__Resume</c>,
/// <c>Auskunft__Companies</c>, <c>Auskunft__Zeitueberschreitung</c>), so they
/// stay as they are until the keys are renamed everywhere at once — in compose,
/// in the chart and in <c>.env.example</c>.
/// </remarks>
public sealed class LookupSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Auskunft";

    /// <summary>identity-service — name and language.</summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>profile-service — headline, text, skills.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>resume-service — the career history.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Auskunft__Resume</c>). It was
    /// renamed to <c>CurriculumVitae</c> once, on its own: compose kept setting
    /// <c>Auskunft__Resume</c>, the address stayed empty, and every suite stayed
    /// green because none of them reads compose. <c>ComposeKeyTests</c> does.
    /// </remarks>
    public string Resume { get; set; } = string.Empty;

    /// <summary>companies-service — the company's name.</summary>
    public string Companies { get; set; } = string.Empty;

    /// <summary>How long a call may take.</summary>
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
/// The public employer profile — the same names as <c>EmployerProfileV1</c>.
/// <c>name</c> instead of <c>display_name</c> always arrived empty, and the
/// letter went out without a company.
/// </summary>
public sealed record CompanyProfileResponse(
    [property: JsonPropertyName("display_name")] string? Name,
    [property: JsonPropertyName("line1")] string? Line1 = null,
    [property: JsonPropertyName("postal_code")] string? PostalCode = null,
    [property: JsonPropertyName("city")] string? City = null,
    [property: JsonPropertyName("country")] string? Country = null);

/// <summary>Fetches one's own details — with the caller's token.</summary>
/// <remarks>
/// <strong>The context is filled server-side, and that is the point.</strong>
/// Letting the browser send it would be shorter and would make the promise of
/// ADR-0034 worthless: "only one's own data" is a claim only the server can
/// keep. It is fetched with the caller's token — so exactly what this person may
/// see anyway, and not one row more.
/// <para>
/// <strong>A missing part is no error.</strong> Whoever has put up no profile
/// and no résumé gets a thinner cover letter, not an error message. Only when a
/// service <em>answers, but unusably</em>, or does not answer at all, does
/// <see cref="ApplicantLookupSilent" /> fly — because then we would not know
/// whether something is missing or merely cannot be had right now.
/// </para>
/// </remarks>
public sealed class HttpApplicantLookup(
    IHttpClientFactory factory,
    IOptions<LookupSettings> settings,
    ICallerToken token) : IApplicantLookup, ICompanyNameLookup, IContactLookup
{
    /// <summary>The name under which the client is registered.</summary>
    public const string ClientName = "lookup";

    private readonly LookupSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<OwnDetails> GetAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetJsonAsync<SessionResponse>(
            _settings.Identity, "/auth/session", cancellationToken);
        var profile = await GetJsonAsync<ProfileResponse>(
            _settings.Profile, "/profiles/me", cancellationToken);
        var resume = await GetJsonAsync<ResumeResponse>(
            _settings.Resume, "/resumes/me", cancellationToken);

        return new OwnDetails(
            FullName(session?.User),
            profile?.Headline ?? string.Empty,
            profile?.Bio ?? string.Empty,
            profile?.Skills ?? [],
            [
                .. (resume?.Positions ?? []).Select(station => new CareerStation(
                    station.Employer ?? string.Empty,
                    station.Title ?? string.Empty,
                    // "seit" instead of an open dash: a running position is a
                    // statement, not a missing value. German, because the
                    // period goes into the German prompt (CoverLetterContext.Rules).
                    station.EndedOn is { Length: > 0 } end
                        ? $"{station.StartedOn} bis {end}"
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
        var profile = await GetJsonAsync<CompanyProfileResponse>(
            _settings.Companies,
            $"/companies/{company.Value}/profile",
            cancellationToken);

        return profile?.Name ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<ApplicantContact> GetContactAsync(CancellationToken cancellationToken = default)
    {
        // A name shared with IApplicantLookup — hence explicit. The postal
        // address must NOT land in OwnDetails; this way is only the snapshot.
        try
        {
            var session = await GetJsonAsync<SessionResponse>(
                _settings.Identity, "/auth/session", cancellationToken);
            var address = await GetJsonAsync<PostalAddressResponse>(
                _settings.Identity, "/account/address", cancellationToken);

            return new ApplicantContact(
                FullName(session?.User),
                address?.Line1 ?? string.Empty,
                address?.Line2 ?? string.Empty,
                address?.PostalCode ?? string.Empty,
                address?.City ?? string.Empty,
                string.IsNullOrEmpty(address?.Country) ? "DE" : address.Country,
                address?.Phone ?? string.Empty,
                session?.User?.Email ?? string.Empty);
        }
        catch (ApplicantLookupSilent)
        {
            return ApplicantContact.Empty;
        }
    }

    /// <summary>
    /// Given and family name, otherwise the display name. Never a postal address.
    /// </summary>
    internal static string FullName(SessionUser? user)
    {
        var full = $"{user?.GivenName} {user?.FamilyName}".Trim();
        return full.Length > 0 ? full : user?.DisplayName ?? string.Empty;
    }

    /// <summary>A GET with the caller's token. <c>null</c> means "nothing there".</summary>
    private async Task<T?> GetJsonAsync<T>(
        string baseAddress, string path, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(baseAddress))
        {
            // Not set up means: this part is missing. That is a configuration
            // situation and no failure — the letter becomes thinner.
            return null;
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(baseAddress), path));

        if (token.Value is { Length: > 0 } bearer)
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
                $"A service holding one's own details does not answer ({error.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApplicantLookupSilent(
                "A service holding one's own details does not answer (timeout).");
        }

        using (response)
        {
            // 404 means "has none" — that is an answer and no failure.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ApplicantLookupSilent(
                    $"A service holding one's own details answers with {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (JsonException error)
            {
                throw new ApplicantLookupSilent(
                    "A service holding one's own details sent an unusable or empty response.",
                    error);
            }
        }
    }
}
