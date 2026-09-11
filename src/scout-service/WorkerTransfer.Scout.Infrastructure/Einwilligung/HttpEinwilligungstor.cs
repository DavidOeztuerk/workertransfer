using System.Net.Http.Headers;
using System.Net.Http.Json;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger erreichbar ist.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    /// <remarks>
    /// Im Compose-Netz der Dienstname, nicht <c>localhost</c> — der Aufruf läuft
    /// von Behälter zu Behälter. Und er steht in der Konfiguration, weil die
    /// Egress-Grenze ihre erlaubten Hosts von dort ableitet
    /// (<c>Dienstgrundlage.GerufeneHosts</c>).
    /// </remarks>
    public string Adresse { get; set; } = "http://consent-service:8002";

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fünf Sekunden. OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>:
    /// HUNDERT Sekunden — das ist kein Zeitlimit, sondern ein hängender Aufruf
    /// mit einem Ende irgendwann, und er hängt den Aufrufer mit.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Fragt den Ledger — synchron, jedes Mal, ohne Zwischenspeicher.</summary>
/// <remarks>
/// <para>Wortgleich zu dem in profile-service, und das ist der Punkt: dieselbe
/// Frage muss dieselbe Antwort bekommen. Zwei Fähigkeiten je Mensch
/// (öffentlich ODER für dieses Unternehmen), in der Reihenfolge der Fragen.</para>
///
/// <para>Gefragt wird mit dem Token des <em>Aufrufers</em> und nicht mit einem
/// eigenen Konto: der Dienst fragt in dessen Auftrag, und das Protokoll des
/// Ledgers soll festhalten, wer wirklich gefragt hat. Der Kopf zuerst, sonst
/// das Cookie — die Oberfläche sieht das <c>httpOnly</c>-Token nie und kann es
/// nur so zurückgeben.</para>
/// </remarks>
public sealed class HttpEinwilligungstor(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<Einwilligungseinstellungen> einstellungen,
    ILogger<HttpEinwilligungstor> protokoll) : IEinwilligungstor
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "consent";

    private readonly Einwilligungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> DarfSehenAlleAsync(
        IReadOnlyList<SubjectId> wer,
        TenantId firma,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wer);

        if (wer.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            // Kein Ledger eingerichtet heisst nicht „alles frei". Es heisst,
            // dass dieser Dienst die Frage nicht beantworten kann.
            throw new EinwilligungSchweigt("Consent:Adresse ist nicht gesetzt.");
        }

        var fuerFirma = Sichtbarkeiten.FuerFirma(firma);

        var paare = wer
            .SelectMany(einer => new[]
            {
                new EinwilligungsfrageV1(einer.Value, Sichtbarkeiten.Oeffentlich),
                new EinwilligungsfrageV1(einer.Value, fuerFirma)
            })
            .ToArray();

        if (paare.Length > Einwilligungsgrenzen.HoechsteSammelgroesse)
        {
            // Der Aufrufer fragt mehr, als der Vertrag trägt. Das ist ein
            // Programmierfehler und keine Auskunft über einen Menschen —
            // deshalb laut, nicht als „nicht freigegeben".
            throw new EinwilligungSchweigt(
                $"{paare.Length} Paare überschreiten "
                + $"{Einwilligungsgrenzen.HoechsteSammelgroesse}");
        }

        var antwort = await Sende(paare, cancellationToken);

        if (antwort.Results.Count != paare.Length)
        {
            throw new EinwilligungSchweigt(
                $"{antwort.Results.Count} Antworten auf {paare.Length} Fragen");
        }

        return
        [
            .. Enumerable.Range(0, wer.Count).Select(stelle =>
                Gilt(antwort.Results[stelle * 2]) || Gilt(antwort.Results[(stelle * 2) + 1]))
        ];
    }

    /// <summary>Eine gelöschte Fähigkeit zieht die Erlaubnis logisch zurück.</summary>
    private static bool Gilt(EinwilligungsantwortV1 antwort) =>
        antwort.Granted && !antwort.Deleted;

    private async Task<EinwilligungssammelantwortV1> Sende(
        IReadOnlyList<EinwilligungsfrageV1> paare, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_einstellungen.Adresse), "/consent/check-batch"))
        {
            Content = JsonContent.Create(new EinwilligungssammelfrageV1(paare))
        };

        if (Aufrufertoken() is { Length: > 0 } token)
        {
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            // Nur die Art, nie der Inhalt: was gefragt wurde, ist eine Liste
            // von Menschen und gehört nicht ins Protokoll.
            protokoll.LogWarning(
                "Der Consent-Ledger ist nicht erreichbar: {Art}", fehler.GetType().Name);

            throw new EinwilligungSchweigt("consent-service unreachable");
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Der Consent-Ledger hat nicht rechtzeitig geantwortet.");

            throw new EinwilligungSchweigt($"consent-service timed out ({fehler.GetType().Name})");
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                // Auch 401 und 403: unser Token taugt nicht — das ist ein
                // Systemproblem und keine Aussage darüber, ob die Person
                // eingewilligt hat.
                protokoll.LogWarning(
                    "Der Consent-Ledger antwortete mit {Status}.", (int)antwort.StatusCode);

                throw new EinwilligungSchweigt(
                    $"consent-service returned {(int)antwort.StatusCode}");
            }

            try
            {
                return await antwort.Content
                           .ReadFromJsonAsync<EinwilligungssammelantwortV1>(cancellationToken)
                       ?? throw new EinwilligungSchweigt("consent-service sent an empty answer");
            }
            catch (Exception fehler) when (fehler is not EinwilligungSchweigt)
            {
                protokoll.LogWarning("Der Consent-Ledger antwortete unverständlich.");

                throw new EinwilligungSchweigt("consent-service sent an unusable answer");
            }
        }
    }

    /// <summary>Das Token des Aufrufers: erst der Kopf, dann das Cookie.</summary>
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
