using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.ServiceDefaults.Rollen;

/// <summary>Wo identity-service für interne Abfragen antwortet.</summary>
/// <remarks>
/// Derselbe Abschnitt und dasselbe Geheimnis wie bei der Mitgliederabfrage von
/// applications-service — ein zweiter Name für dieselbe Adresse wäre der, der
/// beim nächsten Umzug stehen bleibt.
/// </remarks>
public sealed class Firmenrolleneinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Identity";

    /// <summary>Wo identity-service antwortet.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Das gemeinsame Geheimnis der internen Türen.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fünf Sekunden, wie bei jedem anderen Dienst-zu-Dienst-Aufruf hier. OHNE
    /// diese Zeile gilt die Vorgabe von <c>HttpClient</c>: HUNDERT Sekunden —
    /// und ein hängender Dienst hinge dann jede Rechteprüfung mit.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Fragt identity-service nach der Rolle — je Anfrage, ohne Speicher.</summary>
/// <remarks>
/// <para><strong>Kein Zwischenspeicher, und das ist dieselbe Regel wie beim
/// Einwilligungs-Ledger (ADR-0013).</strong> Wer aus einem Unternehmen entfernt
/// wird, ist bei der nächsten Anfrage draussen. Ein Speicher mit einer Lebenszeit
/// wäre genau das Fenster, in dem die Entfernung nicht gilt — und dieses Fenster
/// bräuchte eine Begründung, die niemand geben kann.</para>
///
/// <para>Ruft <c>GET /internal/companies/{firma}/members/{wer}/role</c> mit dem
/// gemeinsamen Geheimniskopf. Liest den geteilten Vertrag, nie ein anonymes
/// Objekt: ein nicht gelesenes Feld wäre hier stillschweigend „kein Admin".</para>
/// </remarks>
public sealed class HttpFirmenrollen(
    IHttpClientFactory fabrik,
    IOptions<Firmenrolleneinstellungen> einstellungen,
    ILogger<HttpFirmenrollen> protokoll) : IFirmenrollen
{
    /// <summary>Der Name des Klienten in der Fabrik.</summary>
    public const string Klient = "identity-rollen";

    private const string Geheimniskopf = "X-Notify-Secret";

    private readonly Firmenrolleneinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Firmenrolle> RolleAsync(
        Guid wer,
        Guid firma,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new RolleSchweigt("Identity:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            throw new RolleSchweigt("Identity:Geheimnis ist nicht gesetzt.");
        }

        using var klient = fabrik.CreateClient(Klient);
        klient.Timeout = _einstellungen.Zeitueberschreitung;

        HttpResponseMessage antwort;

        try
        {
            using var anfrage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(
                    new Uri(_einstellungen.Adresse),
                    $"/internal/companies/{firma}/members/{wer}/role"));
            anfrage.Headers.Add(Geheimniskopf, _einstellungen.Geheimnis);

            antwort = await klient.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            protokoll.LogWarning(
                "Rollenauskunft nicht erreichbar ({Art})", fehler.GetType().Name);
            throw new RolleSchweigt("identity-service (intern) ist nicht erreichbar.", fehler);
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Rollenauskunft antwortete nicht rechtzeitig");
            throw new RolleSchweigt(
                "identity-service (intern) antwortete nicht rechtzeitig.", fehler);
        }

        using (antwort)
        {
            // 404 heisst hier NICHT „kein Mitglied": der interne Endpunkt
            // antwortet 404, wenn das Geheimnis fehlt oder nicht stimmt. Als
            // „keine Rolle" gelesen wuerde ein falsch verdrahtetes Geheimnis
            // ueberall zu einem sauber aussehenden 403 — und niemand suchte
            // danach.
            if (antwort.StatusCode == HttpStatusCode.NotFound)
            {
                throw new RolleSchweigt(
                    "identity-service (intern) nicht erreichbar oder Geheimnis ungültig.");
            }

            if (!antwort.IsSuccessStatusCode)
            {
                throw new RolleSchweigt(
                    $"identity-service (intern) antwortete mit {(int)antwort.StatusCode}.");
            }

            try
            {
                var gelesen = await antwort.Content
                    .ReadFromJsonAsync<FirmenrolleV1>(cancellationToken)
                    ?? throw new RolleSchweigt("identity-service (intern) sandte nichts.");

                return Rollennamen.Lies(gelesen.Rolle);
            }
            catch (JsonException fehler)
            {
                throw new RolleSchweigt(
                    "identity-service (intern) sandte eine unbrauchbare Antwort.", fehler);
            }
        }
    }
}
