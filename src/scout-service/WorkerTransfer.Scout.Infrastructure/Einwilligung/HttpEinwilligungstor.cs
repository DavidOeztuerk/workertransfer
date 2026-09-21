using System.Net.Http.Headers;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger erreichbar ist.</summary>
public sealed class ConsentSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    /// <remarks>
    /// Im Compose-Netz der Dienstname, nicht <c>localhost</c> — der Aufruf läuft
    /// von Behälter zu Behälter. Und er steht in der Konfiguration, weil die
    /// Egress-Grenze ihre erlaubten Hosts von dort ableitet
    /// (<c>ServiceFoundation.GerufeneHosts</c>).
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
/// Ledgers soll festhalten, wer wirklich gefragt hat. Der Header zuerst, sonst
/// das Cookie — die Oberfläche sieht das <c>httpOnly</c>-Token nie und kann es
/// nur so zurückgeben.</para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<ConsentSettings> einstellungen,
    ILogger<HttpConsentGate> protokoll) : IConsentGate
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "consent";

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> MaySeeAllAsync(
        IReadOnlyList<SubjectId> subject,
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (subject.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            // Kein Ledger eingerichtet heisst nicht „alles frei". Es heisst,
            // dass dieser Dienst die Frage nicht beantworten kann.
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        var fuerFirma = Sichtbarkeiten.FuerFirma(company);

        var paare = subject
            .SelectMany(einer => new[]
            {
                new ConsentQuestionV1(einer.Value, Sichtbarkeiten.Oeffentlich),
                new ConsentQuestionV1(einer.Value, fuerFirma)
            })
            .ToArray();

        if (paare.Length > ConsentLimits.HoechsteSammelgroesse)
        {
            // Der Aufrufer fragt mehr, als der Vertrag trägt. Das ist ein
            // Programmierfehler und keine Auskunft über einen Menschen —
            // deshalb laut, nicht als „nicht freigegeben".
            throw new ConsentGateSilent(
                $"{paare.Length} Paare überschreiten "
                + $"{ConsentLimits.HoechsteSammelgroesse}");
        }

        var response = await Sende(paare, cancellationToken);

        if (response.Results.Count != paare.Length)
        {
            throw new ConsentGateSilent(
                $"{response.Results.Count} Antworten auf {paare.Length} Fragen");
        }

        return
        [
            .. Enumerable.Range(0, subject.Count).Select(job =>
                Gilt(response.Results[job * 2]) || Gilt(response.Results[(job * 2) + 1]))
        ];
    }

    /// <summary>Eine gelöschte Fähigkeit zieht die Erlaubnis logisch zurück.</summary>
    private static bool Gilt(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<ConsentBatchResponseV1> Sende(
        IReadOnlyList<ConsentQuestionV1> paare, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_einstellungen.Adresse), "/consent/check-batch"))
        {
            Content = JsonContent.Create(new ConsentBatchQuestionV1(paare))
        };

        if (Aufrufertoken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // Nur die Art, nie der Inhalt: was gefragt wurde, ist eine Liste
            // von Menschen und gehört nicht ins Protokoll.
            protokoll.LogWarning(
                "Der Consent-Ledger ist nicht erreichbar: {Art}", error.GetType().Name);

            throw new ConsentGateSilent("consent-service unreachable");
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Der Consent-Ledger hat nicht rechtzeitig geantwortet.");

            throw new ConsentGateSilent($"consent-service timed out ({error.GetType().Name})");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Auch 401 und 403: unser Token taugt nicht — das ist ein
                // Systemproblem und keine Aussage darüber, ob die Person
                // eingewilligt hat.
                protokoll.LogWarning(
                    "Der Consent-Ledger antwortete mit {Status}.", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service returned {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content
                           .ReadFromJsonAsync<ConsentBatchResponseV1>(cancellationToken)
                       ?? throw new ConsentGateSilent("consent-service sent an empty answer");
            }
            catch (Exception error) when (error is not ConsentGateSilent)
            {
                protokoll.LogWarning("Der Consent-Ledger antwortete unverständlich.");

                throw new ConsentGateSilent("consent-service sent an unusable answer");
            }
        }
    }

    /// <summary>Das Token des Aufrufers: erst der Header, dann das Cookie.</summary>
    private string? Aufrufertoken()
    {
        var context = zugriff.HttpContext;

        if (context is null)
        {
            return null;
        }

        var header = context.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : context.Request.Cookies["access"];
    }
}
