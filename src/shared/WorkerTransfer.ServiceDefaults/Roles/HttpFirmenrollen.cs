using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.ServiceDefaults.Roles;

/// <summary>Where identity-service answers internal queries.</summary>
/// <remarks>
/// The same section and the same secret as for applications-service's member
/// query — a second name for the same address would be the one left behind at
/// the next move.
/// </remarks>
public sealed class CompanyRoleSettings
{
    /// <summary>The section in the configuration.</summary>
    public const string Section = "Identity";

    /// <summary>Where identity-service answers.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>The shared secret of the internal doors.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>How long an answer is waited for.</summary>
    /// <remarks>
    /// Five seconds, as with every other service-to-service call here. WITHOUT
    /// this line the default of <c>HttpClient</c> applies: ONE HUNDRED seconds
    /// — and a hanging service would then hang every permission check with it.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks identity-service for the role — per request, with no store.</summary>
/// <remarks>
/// <para><strong>No cache, and that is the same rule as at the consent ledger
/// (ADR-0013).</strong> Whoever is removed from a company is out at the next
/// request. A store with a lifetime would be exactly the window in which the
/// removal does not apply — and that window would need a justification nobody
/// can give.</para>
///
/// <para>Calls <c>GET /internal/companies/{company}/members/{subject}/role</c>
/// with the shared secret header. Reads the shared contract, never an anonymous
/// object: a field left unread would silently be "not an admin" here.</para>
/// </remarks>
public sealed class HttpCompanyRoles(
    IHttpClientFactory fabrik,
    IOptions<CompanyRoleSettings> einstellungen,
    ILogger<HttpCompanyRoles> protokoll) : ICompanyRoles
{
    /// <summary>The name of the client in the factory.</summary>
    public const string Klient = "identity-rollen";

    private const string Geheimniskopf = "X-Notify-Secret";

    private readonly CompanyRoleSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<CompanyRole> RoleAsync(
        Guid subject,
        Guid company,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new RoleLookupSilent("Identity:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            throw new RoleLookupSilent("Identity:Geheimnis ist nicht gesetzt.");
        }

        using var klient = fabrik.CreateClient(Klient);
        klient.Timeout = _einstellungen.Zeitueberschreitung;

        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(
                    new Uri(_einstellungen.Adresse),
                    $"/internal/companies/{company}/members/{subject}/role"));
            request.Headers.Add(Geheimniskopf, _einstellungen.Geheimnis);

            response = await klient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            protokoll.LogWarning(
                "Rollenauskunft nicht erreichbar ({Art})", error.GetType().Name);
            throw new RoleLookupSilent("identity-service (intern) ist nicht erreichbar.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Rollenauskunft antwortete nicht rechtzeitig");
            throw new RoleLookupSilent(
                "identity-service (intern) antwortete nicht rechtzeitig.", error);
        }

        using (response)
        {
            // 404 does NOT mean "not a member" here: the internal endpoint
            // answers 404 when the secret is missing or wrong. Read as "no
            // role", a mis-wired secret would turn into a clean-looking 403
            // everywhere — and nobody would go looking for it.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RoleLookupSilent(
                    "identity-service (intern) nicht erreichbar oder Geheimnis ungültig.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new RoleLookupSilent(
                    $"identity-service (intern) antwortete mit {(int)response.StatusCode}.");
            }

            try
            {
                var gelesen = await response.Content
                    .ReadFromJsonAsync<CompanyRoleV1>(cancellationToken)
                    ?? throw new RoleLookupSilent("identity-service (intern) sandte nichts.");

                return RoleNames.Lies(gelesen.Rolle);
            }
            catch (JsonException error)
            {
                throw new RoleLookupSilent(
                    "identity-service (intern) sandte eine unbrauchbare Antwort.", error);
            }
        }
    }
}
