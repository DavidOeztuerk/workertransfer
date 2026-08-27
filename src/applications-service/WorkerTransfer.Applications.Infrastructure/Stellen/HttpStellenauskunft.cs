using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Stellen;

/// <summary>Wo der Jobs-Dienst antwortet.</summary>
public sealed class Stelleneinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Jobs";

    /// <summary>Die Basisadresse des Jobs-Dienstes.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange ein Aufruf dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Was <c>GET /jobs/{id}</c> davon zurückgibt, was hier gebraucht wird.</summary>
internal sealed record Stellenantwort(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("title")] string Title);

/// <summary>Fragt den Jobs-Dienst.</summary>
/// <remarks>
/// Über <c>GET /jobs/{id}</c> — den Endpunkt, der bewusst ohne Anmeldung
/// auskommt und Entwurf, geschlossen und nicht vorhanden ununterscheidbar
/// hält.
/// </remarks>
public sealed class HttpStellenauskunft(
    IHttpClientFactory fabrik,
    IOptions<Stelleneinstellungen> einstellungen,
    ILogger<HttpStellenauskunft> protokoll) : IStellenauskunft
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "jobs";

    private readonly Stelleneinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<OeffentlicheStelle?> HoleAsync(
        Guid stelle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new StelleSchweigt("Jobs:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.GetAsync(
                new Uri(new Uri(_einstellungen.Adresse), $"/jobs/{stelle}"), cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            protokoll.LogWarning("Jobs-Dienst nicht erreichbar ({Art})", fehler.GetType().Name);
            throw new StelleSchweigt("jobs-service ist nicht erreichbar.", fehler);
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Jobs-Dienst antwortete nicht rechtzeitig");
            throw new StelleSchweigt("jobs-service antwortete nicht rechtzeitig.", fehler);
        }

        using (antwort)
        {
            // Der einzige Statuscode, der eine Aussage über die Stelle ist.
            if (antwort.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!antwort.IsSuccessStatusCode)
            {
                // Nicht als „gibt es nicht" behandeln: die Person bekäme eine
                // Absage, die niemand ausgesprochen hat.
                throw new StelleSchweigt(
                    $"jobs-service antwortete mit {(int)antwort.StatusCode}.");
            }

            try
            {
                var gelesen = await antwort.Content
                    .ReadFromJsonAsync<Stellenantwort>(cancellationToken)
                    ?? throw new StelleSchweigt("jobs-service sandte nichts.");

                return new OeffentlicheStelle(
                    gelesen.Id, new TenantId(gelesen.TenantId), gelesen.Title);
            }
            catch (JsonException fehler)
            {
                throw new StelleSchweigt(
                    "jobs-service sandte eine unbrauchbare Antwort.", fehler);
            }
        }
    }
}
