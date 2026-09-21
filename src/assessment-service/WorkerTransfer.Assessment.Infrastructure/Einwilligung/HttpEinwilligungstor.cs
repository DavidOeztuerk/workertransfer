using System.Net;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Infrastructure.Sicherheit;
using WorkerTransfer.Contracts.Consent;

namespace WorkerTransfer.Assessment.Infrastructure.Einwilligung;

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
    /// (<c>ServiceFoundation.GerufeneHosts</c>): ein Ziel, das nur im Quelltext
    /// stünde, wird abgewiesen — ohne Protokollzeile, und der Ausfall sähe dann
    /// aus wie ein Ausfall des Ledgers.
    /// </remarks>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fünf Sekunden. OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>:
    /// HUNDERT Sekunden — das ist kein Zeitlimit, sondern ein hängender Aufruf
    /// mit einem Ende irgendwann, und er hängt den Aufrufer mit.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Der Ledger — gelesen, synchron und ohne Zwischenspeicher.</summary>
/// <remarks>
/// <para>Es gibt hier kein Feld, das eine Antwort halten könnte, und keinen
/// <c>IMemoryCache</c> im Konstruktor. Ein Revocation muss bei der nächsten
/// ResumeRequest wirken (ADR-0013) — und in einem Dienst, in dem ein Unternehmen
/// einem Menschen Arbeit aufträgt, ist das kein Leistungsdetail.</para>
///
/// <para><strong>Nur gelesen.</strong> Dieser Dienst erteilt keine Fähigkeit und
/// widerruft keine: er hat keine eigene, und die fremden gehören der Person
/// (ADR-0042).</para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<ConsentSettings> einstellungen,
    ILogger<HttpConsentGate> protokoll) : IConsentGate
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>Wie viele Fragen je Zeile gestellt werden.</summary>
    /// <remarks>
    /// Zwei: „für dieses Unternehmen" und „für alle". Die zweite ist die
    /// weitere — wer öffentlich sichtbar ist, ist es auch für dieses
    /// Unternehmen.
    /// </remarks>
    public const int FragenJeZeile = 2;

    /// <summary>Wie viele Paare eine Runde trägt.</summary>
    /// <remarks>
    /// Abgeleitet, nicht gewählt: der Ledger deckelt eine Sammelfrage bei
    /// hundert Fragen, und je Paar sind es zwei.
    /// </remarks>
    public const int PaareJeRunde = ConsentLimits.HoechsteSammelgroesse / FragenJeZeile;

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        (await MayEachSeeAsync([(subject, company)], cancellationToken))[0];

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> MayEachSeeAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        if (paare.Count == 0)
        {
            return [];
        }

        var duerfen = new List<bool>(paare.Count);

        // Gestueckelt, weil der Ledger eine Sammelfrage deckelt. Mehrere Wege
        // hin und zurueck sind immer noch einer je fuenfzig Zeilen statt zwei
        // je Zeile.
        foreach (var stapel in paare.Chunk(PaareJeRunde))
        {
            var fragen = stapel
                .SelectMany(paar => new[]
                {
                    new ConsentQuestionV1(paar.Wer.Value, Sichtbarkeiten.Profile(paar.Firma)),
                    new ConsentQuestionV1(paar.Wer.Value, Sichtbarkeiten.ProfilOeffentlich)
                })
                .ToArray();

            var response = await SendAsync(
                new ConsentBatchQuestionV1(fragen), cancellationToken);

            // Die Reihenfolge gehoert zum Vertrag, und die Laenge auch. Eine
            // unpassende Antwort wird abgewiesen statt geraten: sie falsch
            // zuzuordnen hiesse, die Release des falschen Menschen zu lesen.
            if (response.Results.Count != fragen.Length)
            {
                throw new ConsentGateSilent(
                    $"{response.Results.Count} Antworten auf {fragen.Length} Fragen");
            }

            for (var i = 0; i < stapel.Length; i++)
            {
                var ab = i * FragenJeZeile;

                duerfen.Add(Gilt(response.Results[ab]) || Gilt(response.Results[ab + 1]));
            }
        }

        return duerfen;
    }

    /// <summary>Eine gelöschte Fähigkeit zieht die Erlaubnis logisch zurück.</summary>
    private static bool Gilt(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<ConsentBatchResponseV1> SendAsync(
        ConsentBatchQuestionV1 body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            // Kein Ledger eingerichtet heisst nicht „alles frei". Es heisst,
            // dass dieser Dienst die Frage nicht beantworten kann.
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/consent/check-batch"))
        {
            Content = JsonContent.Create(body)
        };

        if (token.Wert is { Length: > 0 } value)
        {
            request.Headers.Add("Authorization", $"Bearer {value}");
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // Nur die Art, nie der Inhalt: gefragt wurde nach einem Menschen.
            protokoll.LogWarning(
                "Der Consent-Ledger ist nicht erreichbar: {Art}", error.GetType().Name);

            throw new ConsentGateSilent("consent-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Der Consent-Ledger hat nicht rechtzeitig geantwortet.");

            throw new ConsentGateSilent("consent-service timed out");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                // Auch eine Ablehnung ist hier ein Schweigen: sie sagt etwas
                // ueber DIESEN Aufruf und nichts ueber den Menschen, nach dem
                // gefragt wurde. Sie in ein „nicht freigegeben" zu uebersetzen
                // hiesse, aus unserem Fehler eine Aussage ueber ihn zu machen.
                protokoll.LogWarning(
                    "Der Consent-Ledger hat abgelehnt: {Status}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service refused with {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
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
}
