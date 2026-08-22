using System.Net.Http.Headers;
using System.Net.Http.Json;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Infrastructure.Einwilligung;

/// <summary>Fragt den Consent-Ledger über HTTP.</summary>
/// <remarks>
/// Synchron und ohne Zwischenspeicher (ADR-0013). Ein Widerruf muss beim
/// nächsten Abruf wirken, nicht beim übernächsten — ein Cache wäre hier kein
/// Leistungsdetail, sondern ein Regelbruch.
/// <para>
/// Gefragt wird mit dem Token des <em>Aufrufers</em> und nicht mit einem
/// eigenen Konto: der Dienst fragt in dessen Auftrag. Der Kopf zuerst, sonst
/// das Cookie — die Oberfläche sieht das <c>httpOnly</c>-Token nie und kann es
/// nur so zurückgeben.
/// </para>
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
    public async Task<bool> DarfSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        // Zwei Fragen in einer Runde, obwohl es nur um einen Menschen geht: die
        // öffentliche Freigabe ist der häufige Fall, aber ein zweiter Umlauf
        // für die andere kostet mehr als die zweite Zeile im selben Rumpf.
        var urteile = await Frage([wer], firma, cancellationToken);

        return urteile[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> DarfSehenAlleAsync(
        IReadOnlyList<SubjectId> wer,
        TenantId firma,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wer);

        return wer.Count == 0 ? [] : await Frage(wer, firma, cancellationToken);
    }

    /// <summary>Eine Sammelfrage, zwei Paare je Person, in deren Reihenfolge.</summary>
    private async Task<IReadOnlyList<bool>> Frage(
        IReadOnlyList<SubjectId> wer, TenantId firma, CancellationToken cancellationToken)
    {
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
            // deshalb laut, nicht als „nicht freigegeben“.
            throw new EinwilligungSchweigt(
                $"{paare.Length} Paare überschreiten "
                + $"{Einwilligungsgrenzen.HoechsteSammelgroesse}");
        }

        var antwort = await Sende(paare, cancellationToken);

        if (antwort.Results.Count != paare.Length)
        {
            // Eine Antwort, die nicht zu den Fragen passt, lässt sich nicht
            // zuordnen — und falsch zuzuordnen hieße, das Profil der falschen
            // Person zu zeigen. Also weigern statt raten.
            throw new EinwilligungSchweigt(
                $"{antwort.Results.Count} Antworten auf {paare.Length} Fragen");
        }

        // Zwei Antworten je Person, in der Reihenfolge der Fragen: öffentlich
        // ODER für dieses Unternehmen.
        return [.. Enumerable.Range(0, wer.Count).Select(stelle =>
            Gilt(antwort.Results[stelle * 2]) || Gilt(antwort.Results[(stelle * 2) + 1]))];
    }

    /// <summary>
    /// Eine gelöschte Fähigkeit zieht die Erlaubnis logisch zurück.
    /// </summary>
    /// <remarks>
    /// Beides zu prüfen macht die Absicht auch dann richtig, wenn der Ledger
    /// einmal beides zugleich meldet.
    /// </remarks>
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
            protokoll.LogWarning("Der Consent-Ledger ist nicht erreichbar: {Art}",
                fehler.GetType().Name);

            throw new EinwilligungSchweigt("consent-service unreachable");
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Der Consent-Ledger hat nicht rechtzeitig geantwortet.");

            throw new EinwilligungSchweigt("consent-service timed out", fehler.Message.Length);
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                // Auch 401 und 403: unser Token taugt nicht — das ist ein
                // Systemproblem und keine Aussage darüber, ob die Person
                // eingewilligt hat.
                protokoll.LogWarning("Der Consent-Ledger antwortete mit {Status}.",
                    (int)antwort.StatusCode);

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
