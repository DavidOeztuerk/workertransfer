using System.Net.Http.Json;
using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger antwortet.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Consent";

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

/// <summary>Wie ein Widerruf aussieht.</summary>
/// <remarks>
/// Der Ledger verlangt für einen Widerruf eine Begründung und für eine
/// Erteilung keine: etwas wegzunehmen muss erklärbar sein, es zu geben nicht.
/// </remarks>
internal sealed record Widerruf(Guid SubjectId, string Capability, string Reason);

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
public sealed class HttpEinwilligungstor(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<Einwilligungseinstellungen> einstellungen,
    ILogger<HttpEinwilligungstor> protokoll) : IEinwilligungstor
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>Snake_case in beide Richtungen — die Form, die der Ledger spricht.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Begruendung = "Zugriff auf den Marktstatus zurückgezogen";

    private readonly Einwilligungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public Task<bool> DarfProfilSehenAsync(
        SubjectId wer, CancellationToken cancellationToken = default) =>
        ErteiltAsync(wer, Einwilligungsschluessel.Profil, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DarfMarktSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default) =>
        ErteiltAsync(wer, Einwilligungsschluessel.Markt(firma), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> DuerfenMarktSehenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        var urteile = new List<bool>(paare.Count);

        // Gestückelt, weil der Ledger eine Sammelfrage deckelt. Mehrere Wege
        // hin und zurück sind immer noch einer je hundert statt einer je Zeile.
        foreach (var stapel in paare.Chunk(Einwilligungsgrenzen.HoechsteSammelgroesse))
        {
            var frage = new EinwilligungssammelfrageV1(
                [.. stapel.Select(paar => new EinwilligungsfrageV1(
                    paar.Wer.Value, Einwilligungsschluessel.Markt(paar.Firma)))]);

            var antwort = await FrageAsync<EinwilligungssammelfrageV1,
                EinwilligungssammelantwortV1>("/consent/check-batch", frage, cancellationToken);

            // Die Reihenfolge gehört zum Vertrag, und die Länge auch. Eine
            // unpassende Antwort wird abgewiesen statt geraten: sie falsch
            // zuzuordnen hieße, die Freigabe des falschen Menschen zu zeigen.
            if (antwort.Results.Count != stapel.Length)
            {
                throw new EinwilligungSchweigt(
                    $"Der Ledger beantwortete {stapel.Length} Fragen mit "
                    + $"{antwort.Results.Count} Antworten.");
            }

            urteile.AddRange(antwort.Results.Select(Gilt));
        }

        return urteile;
    }

    /// <inheritdoc />
    public async Task ErteileAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default) =>
        await FrageAsync<Erteilung, JsonElement>(
            "/consent/grant",
            new Erteilung(wer.Value, Einwilligungsschluessel.Markt(firma)),
            cancellationToken);

    /// <inheritdoc />
    public async Task WiderrufeAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default) =>
        await FrageAsync<Widerruf, JsonElement>(
            "/consent/revoke",
            new Widerruf(wer.Value, Einwilligungsschluessel.Markt(firma), Begruendung),
            cancellationToken);

    /// <summary>Erteilt und nicht gelöscht. Alles andere ist ein Nein.</summary>
    private static bool Gilt(EinwilligungsantwortV1 antwort) =>
        antwort.Granted && !antwort.Deleted;

    private async Task<bool> ErteiltAsync(
        SubjectId wer, string faehigkeit, CancellationToken cancellationToken)
    {
        var antwort = await FrageAsync<EinwilligungsfrageV1, EinwilligungsantwortV1>(
            "/consent/check",
            new EinwilligungsfrageV1(wer.Value, faehigkeit),
            cancellationToken);

        return Gilt(antwort);
    }

    private async Task<TAntwort> FrageAsync<TFrage, TAntwort>(
        string pfad, TFrage rumpf, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new EinwilligungSchweigt("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), pfad))
        {
            Content = JsonContent.Create(rumpf, options: Format)
        };

        if (token.Wert is { Length: > 0 } wert)
        {
            anfrage.Headers.Add("Authorization", $"Bearer {wert}");
        }

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            protokoll.LogWarning("Consent-Ledger nicht erreichbar ({Art})", fehler.GetType().Name);
            throw new EinwilligungSchweigt("consent-service ist nicht erreichbar.", fehler);
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Consent-Ledger antwortete nicht rechtzeitig");
            throw new EinwilligungSchweigt("consent-service antwortete nicht rechtzeitig.", fehler);
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                // 401 und 403 eingeschlossen: unser Token taugt nicht — ein
                // Systemproblem, und keine Aussage darüber, ob die Person
                // eingewilligt hat.
                protokoll.LogWarning(
                    "Consent-Ledger antwortete mit {Code}", (int)antwort.StatusCode);

                throw new EinwilligungSchweigt(
                    $"consent-service antwortete mit {(int)antwort.StatusCode}.");
            }

            try
            {
                return await antwort.Content.ReadFromJsonAsync<TAntwort>(
                           Format, cancellationToken)
                       ?? throw new EinwilligungSchweigt("consent-service sandte nichts.");
            }
            catch (JsonException fehler)
            {
                throw new EinwilligungSchweigt(
                    "consent-service sandte eine unbrauchbare Antwort.", fehler);
            }
        }
    }
}
