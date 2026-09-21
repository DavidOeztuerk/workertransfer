using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger antwortet.</summary>
public sealed class ConsentSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange eine Frage dauern darf.</summary>
    /// <remarks>
    /// Kurz mit Absicht. Ein schweigender Ledger muss schnell zu einem 503
    /// werden; an ihm zu hängen machte aus einer langsamen Abhängigkeit einen
    /// Dienst, der gar nichts mehr beantwortet.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Wie eine Erteilung auf der Leitung aussieht.</summary>
internal sealed record Erteilung(Guid SubjectId, string Capability);

/// <summary>Wie ein Revocation aussieht.</summary>
/// <remarks>
/// Der Ledger verlangt für einen Revocation eine Begründung und für eine
/// Erteilung keine: etwas wegzunehmen muss erklärbar sein, es zu geben nicht.
/// </remarks>
internal sealed record Revocation(Guid SubjectId, string Capability, string Reason);

/// <summary>Fragt den Ledger, und schreibt in ihn.</summary>
/// <remarks>
/// Synchron und ohne Zwischenspeicher (ADR-0013). Es gibt hier kein Feld, das
/// eine Antwort halten könnte, und keinen <c>IMemoryCache</c> im Konstruktor:
/// bei dieser Angabe wäre ein Zwischenspeicher kein Leistungsdetail, sondern
/// ein gebrochenes Versprechen.
/// <para>
/// Jeder Aufruf reist mit dem Token <em>des Aufrufers</em> statt mit einem
/// Dienstkonto, damit das Protokoll des Ledgers festhält, wer wirklich gefragt
/// hat.
/// </para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<ConsentSettings> einstellungen,
    ILogger<HttpConsentGate> protokoll) : IConsentGate
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>Snake_case in beide Richtungen — die Form, die der Ledger spricht.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Begruendung = "Zugriff auf den Marktstatus zurückgezogen";

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public Task<bool> MaySeeProfileAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.Profile, cancellationToken);

    /// <inheritdoc />
    public Task<bool> MaySeeMarketAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.Markt(company), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> MayEachSeeMarketAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        var urteile = new List<bool>(paare.Count);

        // Gestückelt, weil der Ledger eine Sammelfrage deckelt. Mehrere Wege
        // hin und zurück sind immer noch einer je hundert statt einer je Zeile.
        foreach (var stapel in paare.Chunk(ConsentLimits.HoechsteSammelgroesse))
        {
            var frage = new ConsentBatchQuestionV1(
                [.. stapel.Select(paar => new ConsentQuestionV1(
                    paar.Wer.Value, ConsentKey.Markt(paar.Firma)))]);

            var response = await AskAsync<ConsentBatchQuestionV1,
                ConsentBatchResponseV1>("/consent/check-batch", frage, cancellationToken);

            // Die Reihenfolge gehört zum Vertrag, und die Länge auch. Eine
            // unpassende Antwort wird abgewiesen statt geraten: sie falsch
            // zuzuordnen hieße, die Release des falschen Menschen zu zeigen.
            if (response.Results.Count != stapel.Length)
            {
                throw new ConsentGateSilent(
                    $"Der Ledger beantwortete {stapel.Length} Fragen mit "
                    + $"{response.Results.Count} Antworten.");
            }

            urteile.AddRange(response.Results.Select(Gilt));
        }

        return urteile;
    }

    /// <inheritdoc />
    public async Task GrantAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        await AskAsync<Erteilung, JsonElement>(
            "/consent/grant",
            new Erteilung(subject.Value, ConsentKey.Markt(company)),
            cancellationToken);

    /// <inheritdoc />
    public async Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        await AskAsync<Revocation, JsonElement>(
            "/consent/revoke",
            new Revocation(subject.Value, ConsentKey.Markt(company), Begruendung),
            cancellationToken);

    /// <summary>Erteilt und nicht gelöscht. Alles andere ist ein Nein.</summary>
    private static bool Gilt(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<bool> GrantedAsync(
        SubjectId subject, string skill, CancellationToken cancellationToken)
    {
        var response = await AskAsync<ConsentQuestionV1, ConsentResponseV1>(
            "/consent/check",
            new ConsentQuestionV1(subject.Value, skill),
            cancellationToken);

        return Gilt(response);
    }

    private async Task<TAntwort> AskAsync<TFrage, TAntwort>(
        string path, TFrage body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), path))
        {
            Content = JsonContent.Create(body, options: Format)
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
            protokoll.LogWarning("Consent-Ledger nicht erreichbar ({Art})", error.GetType().Name);
            throw new ConsentGateSilent("consent-service ist nicht erreichbar.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Consent-Ledger antwortete nicht rechtzeitig");
            throw new ConsentGateSilent("consent-service antwortete nicht rechtzeitig.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 und 403 eingeschlossen: unser Token taugt nicht — ein
                // Systemproblem, und keine Aussage darüber, ob die Person
                // eingewilligt hat.
                protokoll.LogWarning(
                    "Consent-Ledger antwortete mit {Code}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service antwortete mit {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TAntwort>(
                           Format, cancellationToken)
                       ?? throw new ConsentGateSilent("consent-service sandte nichts.");
            }
            catch (JsonException error)
            {
                throw new ConsentGateSilent(
                    "consent-service sandte eine unbrauchbare Antwort.", error);
            }
        }
    }
}
