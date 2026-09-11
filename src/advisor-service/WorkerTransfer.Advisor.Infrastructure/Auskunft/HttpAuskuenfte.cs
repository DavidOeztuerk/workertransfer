using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;

namespace WorkerTransfer.Advisor.Infrastructure.Auskunft;

/// <summary>Wo identity-service seine internen Türen hat.</summary>
public sealed class Auskunftseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Identity";

    /// <summary>Die Basisadresse von identity-service.</summary>
    /// <remarks>
    /// Steht in der Konfiguration und nicht im Quelltext, weil die Egress-Grenze
    /// ihre erlaubten Hosts von dort ableitet — ein Ziel, das nur hier im Code
    /// stünde, wird abgewiesen, und zwar ohne Protokollzeile.
    /// </remarks>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Das geteilte Geheimnis der internen Türen.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Ohne diese Zeile gilt die Vorgabe von <c>HttpClient</c>: hundert
    /// Sekunden. Das ist kein Zeitlimit, sondern ein hängender Aufruf.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Was die interne Tür über ein Unternehmen sagt.</summary>
internal sealed record FirmenantwortV1(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("domain")] string? Domain);

/// <summary>Was sie über einen Menschen sagt.</summary>
internal sealed record PersonenantwortV1(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email);

/// <summary>Fragt identity-service — hinter dem geteilten Geheimnis.</summary>
/// <remarks>
/// <para><strong>Zwei Türen, zwei sehr verschiedene Fragen.</strong> Die
/// Firmenauskunft nennt eine Domain, damit ein Ausschluss überhaupt greifen
/// kann; die Personenauskunft nennt einen bürgerlichen Namen, und die wird erst
/// gerufen, <em>nachdem</em> der Ledger Stufe 3 bestätigt hat.</para>
///
/// <para>Mit dem Dienstgeheimnis und nicht mit dem Token des Aufrufers: der
/// Aufrufer ist hier das <em>Unternehmen</em>, und es darf den Klarnamen eines
/// Menschen nicht aus eigener Kraft abrufen können. Was es darf, entscheidet
/// eine Zeile höher der Ledger — und deshalb ist diese Tür so eng: zwei Felder,
/// keine Anschrift, kein Telefon.</para>
/// </remarks>
public sealed class HttpAuskuenfte(
    IHttpClientFactory fabrik,
    IOptions<Auskunftseinstellungen> einstellungen,
    ILogger<HttpAuskuenfte> protokoll) : IFirmenauskunft, IPersonenauskunft
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "identity";

    private const string Geheimniskopf = "X-Notify-Secret";

    private readonly Auskunftseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Firmenbild?> HoleAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var antwort = await LiesAsync<FirmenantwortV1>(
            $"/internal/companies/{firma.Value}", cancellationToken);

        return antwort is null ? null : new Firmenbild(antwort.Name ?? string.Empty,
            antwort.Domain ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<Personenbild?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var antwort = await LiesAsync<PersonenantwortV1>(
            $"/internal/account/{wer.Value}/identity", cancellationToken);

        return antwort is null ? null : new Personenbild(antwort.Name ?? string.Empty,
            antwort.Email ?? string.Empty);
    }

    /// <summary>Ein GET hinter dem Geheimnis. <c>null</c> heisst „gibt es nicht".</summary>
    private async Task<T?> LiesAsync<T>(string pfad, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse)
            || string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            // Nicht eingerichtet ist ein SYSTEMzustand und keine Aussage ueber
            // einen Menschen. Still `null` zu antworten hiesse hier „diese Firma
            // gibt es nicht" — und ein Ausschluss griffe dann nie.
            throw new AuskunftSchweigt("Identity:Adresse oder :Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Geduld;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_einstellungen.Adresse), pfad));

        anfrage.Headers.Add(Geheimniskopf, _einstellungen.Geheimnis);

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            protokoll.LogWarning(
                "identity-service ist nicht erreichbar: {Art}", fehler.GetType().Name);

            throw new AuskunftSchweigt("identity-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("identity-service hat nicht rechtzeitig geantwortet.");

            throw new AuskunftSchweigt("identity-service timed out");
        }

        using (antwort)
        {
            if (antwort.StatusCode == HttpStatusCode.NotFound)
            {
                // Die Tuer antwortet 404 auch dann, wenn das Geheimnis nicht
                // passt — absichtlich, damit sie sich nicht ueber eine Methode
                // verraet. Hier heisst es deshalb nur: keine Auskunft.
                return null;
            }

            if (!antwort.IsSuccessStatusCode)
            {
                protokoll.LogWarning(
                    "identity-service antwortete mit {Status}.", (int)antwort.StatusCode);

                throw new AuskunftSchweigt(
                    $"identity-service returned {(int)antwort.StatusCode}");
            }

            try
            {
                return await antwort.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (Exception fehler) when (fehler is not AuskunftSchweigt)
            {
                protokoll.LogWarning("identity-service antwortete unverständlich.");

                throw new AuskunftSchweigt("identity-service sent an unusable answer");
            }
        }
    }
}
