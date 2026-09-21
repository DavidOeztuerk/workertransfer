using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Infrastructure.Sicherheit;
using WorkerTransfer.Contracts.Consent;

namespace WorkerTransfer.Advisor.Infrastructure.Einwilligung;

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
    /// stünde, wird abgewiesen — ohne Protokollzeile.
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

/// <summary>Wie eine Erteilung auf der Leitung aussieht.</summary>
internal sealed record Erteilung(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability);

/// <summary>Wie ein Revocation aussieht.</summary>
/// <remarks>
/// Der Ledger verlangt für einen Revocation eine Begründung und für eine
/// Erteilung keine: etwas wegzunehmen muss erklärbar sein, es zu geben nicht.
/// </remarks>
internal sealed record Revocation(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// Der Ledger — gelesen und beschrieben, beides synchron und ohne
/// Zwischenspeicher.
/// </summary>
/// <remarks>
/// <para>Es gibt hier kein Feld, das eine Antwort halten könnte, und keinen
/// <c>IMemoryCache</c> im Konstruktor. In einem Dienst, dessen ganzer Zweck
/// gestufte Sichtbarkeit ist, wäre ein Zwischenspeicher kein Leistungsdetail,
/// sondern der Bruch der Zusage: ein Revocation muss bei der nächsten ResumeRequest
/// wirken (ADR-0013).</para>
///
/// <para><strong>Wessen Subjekt gefragt oder geschrieben wird, steht nie im
/// Rumpf dieses Dienstes — es steht im Token.</strong> Erteilen und Widerrufen
/// schicken die Kennung, die im geprüften Token des Aufrufers steht; der Ledger
/// vergleicht sie selbst und antwortet 403 „a consent belongs to its subject",
/// wenn sie nicht passt. Deshalb kann eine Stufenfreigabe nur gelingen, wenn
/// die Person sie auslöst.</para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    ICurrentPrincipal akteur,
    IOptions<ConsentSettings> einstellungen,
    ILogger<HttpConsentGate> protokoll) : IConsentGate
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>
    /// Wie viele Paare eine Runde trägt.
    /// </summary>
    /// <remarks>
    /// Abgeleitet, nicht gewählt: je Paar werden vier Fähigkeiten gefragt, und
    /// der Ledger deckelt eine Sammelfrage bei hundert Paaren. Vier mal
    /// fünfundzwanzig ist hundert.
    /// </remarks>
    public const int PaareJeRunde = ConsentLimits.HoechsteSammelgroesse / 4;

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Stufe>> StagesAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        if (paare.Count == 0)
        {
            return [];
        }

        var stages = new List<Stufe>(paare.Count);

        // Gestueckelt, weil der Ledger eine Sammelfrage deckelt. Mehrere Wege
        // hin und zurueck sind immer noch einer je fuenfundzwanzig Zeilen statt
        // vier je Zeile.
        foreach (var stapel in paare.Chunk(PaareJeRunde))
        {
            var fragen = stapel
                .SelectMany(paar => Faehigkeiten(paar.Firma)
                    .Select(skill => new ConsentQuestionV1(paar.Wer.Value, skill)))
                .ToArray();

            var response = await SendAsync<ConsentBatchQuestionV1,
                ConsentBatchResponseV1>(
                "/consent/check-batch",
                new ConsentBatchQuestionV1(fragen),
                cancellationToken);

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
                stages.Add(Lies(response.Results, i * Anzahl));
            }
        }

        return stages;
    }

    /// <inheritdoc />
    public async Task GrantAsync(
        IReadOnlyList<string> skills, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skills);

        var subject = Selbst();

        foreach (var skill in skills)
        {
            await SendAsync<Erteilung, JsonElement>(
                "/consent/grant", new Erteilung(subject, skill), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        IReadOnlyList<string> skills,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skills);

        var subject = Selbst();

        foreach (var skill in skills)
        {
            await SendAsync<Revocation, JsonElement>(
                "/consent/revoke", new Revocation(subject, skill, reason), cancellationToken);
        }
    }

    /// <summary>Die vier Fragen je Paar, in fester Reihenfolge.</summary>
    /// <remarks>
    /// Die Reihenfolge ist der Vertrag zwischen dieser Methode und
    /// <see cref="Lies"/>; sie zu ändern, ohne dort zu zählen, hiesse, die
    /// Antwort auf „Resume" als „Profil" zu lesen.
    /// </remarks>
    private static string[] Faehigkeiten(TenantId company) =>
    [
        StageCapabilities.Profile(company),
        StageCapabilities.ProfilOeffentlich,
        StageCapabilities.Resume(company),
        StageCapabilities.Klarname(company)
    ];

    private const int Anzahl = 4;

    /// <summary>Aus vier Responses eine Stufe — kumulativ.</summary>
    /// <remarks>
    /// Eine Stufe ist ein <em>State</em> und kein Sprung: wer Stufe 1
    /// zurückgenommen hat, steht nicht auf 3, auch wenn die Fähigkeit von
    /// Stufe 3 noch steht. Der Resume sichtbar und das Profile nicht wäre
    /// eine Erlaubnis ohne Grundlage.
    /// </remarks>
    private static Stufe Lies(IReadOnlyList<ConsentResponseV1> responses, int ab)
    {
        var profile = Gilt(responses[ab]) || Gilt(responses[ab + 1]);
        var resume = Gilt(responses[ab + 2]);
        var klarname = Gilt(responses[ab + 3]);

        if (!profile)
        {
            return Stufe.None;
        }

        if (!resume)
        {
            return Stufe.Profile;
        }

        return klarname ? Stufe.Person : Stufe.Unterlagen;
    }

    /// <summary>Eine gelöschte Fähigkeit zieht die Erlaubnis logisch zurück.</summary>
    private static bool Gilt(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    /// <summary>Wer gerade handelt — aus dem geprüften Token, nie aus dem Rumpf.</summary>
    private Guid Selbst() =>
        akteur.Current is { } handelnder
            ? handelnder.Subject.Value
            : throw new ConsentRefused("not authenticated");

    private async Task<TAntwort> SendAsync<TFrage, TAntwort>(
        string path, TFrage body, CancellationToken cancellationToken)
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
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), path))
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
                // Der Ledger HAT geantwortet, und zwar mit Nein — das ist etwas
                // anderes als sein Schweigen. Es passiert genau dann, wenn
                // jemand fuer einen fremden Menschen erteilen wollte.
                protokoll.LogWarning(
                    "Der Consent-Ledger hat abgelehnt: {Status}", (int)response.StatusCode);

                throw new ConsentRefused(
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
                           .ReadFromJsonAsync<TAntwort>(cancellationToken)
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
