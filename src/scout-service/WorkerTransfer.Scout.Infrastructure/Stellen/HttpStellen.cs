using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Treffer;

namespace WorkerTransfer.Scout.Infrastructure.Stellen;

/// <summary>Wo die Anzeigen liegen.</summary>
public sealed class Stelleneinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Jobs";

    /// <summary>Die Basisadresse von jobs-service.</summary>
    /// <remarks>
    /// Steht in der Konfiguration, weil die Egress-Grenze ihre erlaubten Hosts
    /// von dort ableitet (<c>Dienstgrundlage.GerufeneHosts</c>). Ein Ziel, das
    /// nur im Quelltext steht, wird abgewiesen — ohne Protokollzeile.
    /// </remarks>
    public string Adresse { get; set; } = "http://jobs-service:8006";

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Fragt jobs-service nach dem, was eine Anzeige über ihren Weg sagt.</summary>
/// <remarks>
/// <para><strong>Die öffentliche Route, kein Geheimnis.</strong> Eine
/// veröffentlichte Anzeige ist eine Aussage des Unternehmens an alle;
/// <c>GET /jobs/{id}</c> beantwortet sie ohne Anmeldung. Eine eigene interne Tür
/// dafür wäre eine zweite Wahrheit über dieselbe Frage.</para>
///
/// <para>Gelesen werden drei Felder: Ort, Postleitzahl und der Remotegrad.
/// <strong>Nicht</strong> Titel, Beschreibung oder Fähigkeiten — was dieser
/// Dienst nicht bekommt, kann er nicht weiterreichen (ADR-0004).</para>
/// </remarks>
public sealed class HttpStellen(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<Stelleneinstellungen> einstellungen,
    ILogger<HttpStellen> protokoll) : IStellen
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "jobs";

    private readonly Stelleneinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Stellenaussage?> HoleAsync(
        Guid stelle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new StelleSchweigt("Jobs:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_einstellungen.Adresse), $"/jobs/{stelle}"));

        // Im Namen des Aufrufers: eine Anzeige im Entwurf sieht nur ihr eigenes
        // Unternehmen, und genau dieselbe Entscheidung soll hier fallen.
        if (Aufrufertoken() is { Length: > 0 } token)
        {
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (Exception fehler) when (fehler is HttpRequestException or TaskCanceledException)
        {
            // Die Art, nie der Inhalt.
            protokoll.LogWarning(
                "jobs-service ist nicht erreichbar: {Art}", fehler.GetType().Name);

            throw new StelleSchweigt("jobs-service unreachable");
        }

        using (antwort)
        {
            if (antwort.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                // Es gibt sie nicht, oder nicht fuer diesen Aufrufer. Beides
                // heisst hier: keine Aussage — und daraus wird ein Strich, kein
                // Kreuz.
                return null;
            }

            if (!antwort.IsSuccessStatusCode)
            {
                protokoll.LogWarning(
                    "jobs-service antwortete mit {Status}.", (int)antwort.StatusCode);

                throw new StelleSchweigt($"jobs-service returned {(int)antwort.StatusCode}");
            }

            try
            {
                var gelesen = await antwort.Content
                    .ReadFromJsonAsync<FremdstelleV1>(cancellationToken);

                return gelesen is null
                    ? throw new StelleSchweigt("jobs-service sent an empty answer")
                    : new Stellenaussage(
                        gelesen.Location ?? string.Empty,
                        gelesen.PostalCode ?? string.Empty,
                        Erreichbarkeitsworte.Anwesenheit(gelesen.RemoteMode));
            }
            catch (Exception fehler) when (fehler is JsonException or NotSupportedException)
            {
                protokoll.LogWarning("jobs-service antwortete unverständlich.");

                throw new StelleSchweigt("jobs-service sent an unusable answer");
            }
        }
    }

    private string? Aufrufertoken()
    {
        var kontext = zugriff.HttpContext;

        if (kontext is null)
        {
            return null;
        }

        var kopf = kontext.Request.Headers.Authorization.ToString();

        return kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? kopf["Bearer ".Length..].Trim()
            : kontext.Request.Cookies["access"];
    }
}
