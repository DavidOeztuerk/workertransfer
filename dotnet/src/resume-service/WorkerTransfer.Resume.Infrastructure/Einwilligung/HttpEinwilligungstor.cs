using System.Net.Http.Json;
using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Infrastructure.Einwilligung;

/// <summary>What a grant looks like on the wire.</summary>
internal sealed record Erteilung(Guid SubjectId, string Capability);

/// <summary>What a withdrawal looks like on the wire.</summary>
/// <remarks>
/// The ledger requires a reason for a withdrawal and none for a grant: taking
/// something away has to be explicable, giving it does not. The default says
/// honestly where the withdrawal was triggered instead of inventing one.
/// </remarks>
internal sealed record Widerruf(Guid SubjectId, string Capability, string Reason);

/// <summary>Asks the ledger, and writes to it.</summary>
/// <remarks>
/// Synchronous and without a cache (ADR-0013). There is no field here that
/// could hold an answer and no <c>IMemoryCache</c> in the constructor, and that
/// is deliberate: a withdrawal has to take effect on the very next read, so a
/// cache would not be a performance detail but a broken promise.
/// <para>
/// Every call travels with the <em>caller's</em> token rather than a service
/// account, so the ledger's own log records who really asked.
/// </para>
/// </remarks>
public sealed class HttpEinwilligungstor(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<Einwilligungseinstellungen> einstellungen,
    ILogger<HttpEinwilligungstor> protokoll) : IEinwilligungstor
{
    /// <summary>The name the client is registered under.</summary>
    public const string Klient = "consent";

    /// <summary>Snake_case in both directions — the shape the ledger speaks.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Begruendung = "Freigabe des Lebenslaufs zurückgezogen";

    private readonly Einwilligungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public Task<bool> DarfProfilSehenAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default) =>
        ErteiltAsync(wer, Einwilligungsschluessel.Profil, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DarfLebenslaufLesenAsync(
        SubjectId wer,
        TenantId firma,
        CancellationToken cancellationToken = default) =>
        ErteiltAsync(wer, Einwilligungsschluessel.Lebenslauf(firma), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> DuerfenLebenslaufLesenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        var urteile = new List<bool>(paare.Count);

        // Chunked, because the ledger caps a batch and a person may hold more
        // releases than that. Several round trips are still one per hundred
        // instead of one per row.
        foreach (var stapel in paare.Chunk(Einwilligungsgrenzen.HoechsteSammelgroesse))
        {
            var frage = new EinwilligungssammelfrageV1(
                [.. stapel.Select(paar => new EinwilligungsfrageV1(
                    paar.Wer.Value, Einwilligungsschluessel.Lebenslauf(paar.Firma)))]);

            var antwort = await FrageAsync<EinwilligungssammelfrageV1,
                EinwilligungssammelantwortV1>("/consent/check-batch", frage, cancellationToken);

            // The order is part of the contract, and so is the length. A
            // mismatched answer is refused rather than guessed at: misaligning
            // them would show the wrong person's release.
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
        SubjectId wer,
        TenantId firma,
        CancellationToken cancellationToken = default) =>
        await FrageAsync<Erteilung, JsonElement>(
            "/consent/grant",
            new Erteilung(wer.Value, Einwilligungsschluessel.Lebenslauf(firma)),
            cancellationToken);

    /// <inheritdoc />
    public async Task WiderrufeAsync(
        SubjectId wer,
        TenantId firma,
        CancellationToken cancellationToken = default) =>
        await FrageAsync<Widerruf, JsonElement>(
            "/consent/revoke",
            new Widerruf(wer.Value, Einwilligungsschluessel.Lebenslauf(firma), Begruendung),
            cancellationToken);

    /// <summary>Granted and not erased. Anything else is a no.</summary>
    private static bool Gilt(EinwilligungsantwortV1 antwort) =>
        antwort.Granted && !antwort.Deleted;

    private async Task<bool> ErteiltAsync(
        SubjectId wer,
        string faehigkeit,
        CancellationToken cancellationToken)
    {
        var antwort = await FrageAsync<EinwilligungsfrageV1, EinwilligungsantwortV1>(
            "/consent/check",
            new EinwilligungsfrageV1(wer.Value, faehigkeit),
            cancellationToken);

        return Gilt(antwort);
    }

    private async Task<TAntwort> FrageAsync<TFrage, TAntwort>(
        string pfad,
        TFrage rumpf,
        CancellationToken cancellationToken)
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
                // 401 and 403 included: our token is no good — a system
                // problem, and not a statement about whether the person
                // consented.
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
