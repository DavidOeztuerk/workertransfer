using System.Net.Http.Json;
using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Einwilligung;

/// <summary>Wie eine Erteilung auf der Leitung aussieht.</summary>
internal sealed record Erteilung(Guid SubjectId, string Capability);

/// <summary>Wie ein Widerruf aussieht.</summary>
/// <remarks>
/// Der Ledger verlangt für einen Widerruf eine Begründung und für eine
/// Erteilung keine: etwas wegzunehmen muss erklärbar sein, es zu geben nicht.
/// Die hier sagt ehrlich, wodurch der Widerruf ausgelöst wurde, statt eine zu
/// erfinden.
/// </remarks>
internal sealed record Widerruf(Guid SubjectId, string Capability, string Reason);

/// <summary>Schreibt in den Ledger.</summary>
/// <remarks>
/// Jeder Aufruf reist mit dem Token <em>des Aufrufers</em> statt mit einem
/// Dienstkonto, damit das Protokoll des Ledgers festhält, wer wirklich gefragt
/// hat. Ein fremder Akteur wird dort abgewiesen — was richtig ist: die
/// Freigabe erteilt die Person, nicht dieser Dienst.
/// </remarks>
public sealed class HttpEinwilligungsschreiber(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<Einwilligungseinstellungen> einstellungen,
    ILogger<HttpEinwilligungsschreiber> protokoll) : IEinwilligungsschreiber
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>Snake_case in beide Richtungen — die Form, die der Ledger spricht.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Begruendung = "Bewerbung zurückgezogen";

    private readonly Einwilligungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task ErteileAsync(
        SubjectId wer,
        IReadOnlyList<string> faehigkeiten,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faehigkeiten);

        foreach (var faehigkeit in faehigkeiten)
        {
            await SchreibeAsync(
                "/consent/grant", new Erteilung(wer.Value, faehigkeit), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task WiderrufeAsync(
        SubjectId wer,
        IReadOnlyList<string> faehigkeiten,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faehigkeiten);

        foreach (var faehigkeit in faehigkeiten)
        {
            await SchreibeAsync(
                "/consent/revoke",
                new Widerruf(wer.Value, faehigkeit, Begruendung),
                cancellationToken);
        }
    }

    private async Task SchreibeAsync<TRumpf>(
        string pfad,
        TRumpf rumpf,
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
                // 401 und 403 eingeschlossen: unser Token taugt nicht — ein
                // Systemproblem, und keine Aussage darüber, ob die Person
                // eingewilligt hat.
                protokoll.LogWarning(
                    "Consent-Ledger antwortete mit {Code}", (int)antwort.StatusCode);

                throw new EinwilligungSchweigt(
                    $"consent-service antwortete mit {(int)antwort.StatusCode}.");
            }
        }
    }
}
