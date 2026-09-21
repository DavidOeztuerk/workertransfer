using System.Net.Http.Headers;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Infrastructure.Einwilligung;

/// <summary>Fragt den Consent-Ledger über HTTP.</summary>
/// <remarks>
/// Synchron und ohne Zwischenspeicher (ADR-0013). Ein Revocation muss beim
/// nächsten Abruf wirken, nicht beim übernächsten — ein Cache wäre hier kein
/// Leistungsdetail, sondern ein Regelbruch.
/// <para>
/// Gefragt wird mit dem Token des <em>Aufrufers</em> und nicht mit einem
/// eigenen Konto: der Dienst fragt in dessen Auftrag. Der Header zuerst, sonst
/// das Cookie — die Oberfläche sieht das <c>httpOnly</c>-Token nie und kann es
/// nur so zurückgeben.
/// </para>
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
    public async Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        // Zwei Fragen in einer Runde, obwohl es nur um einen Menschen geht: die
        // öffentliche Release ist der häufige Fall, aber ein zweiter Umlauf
        // für die andere kostet mehr als die zweite Zeile im selben Rumpf.
        //
        // `Frage` traegt weiterhin eine Liste, obwohl nur noch EIN Aufrufer
        // kommt: sie beantwortet die Paarbildung und die Laengenpruefung, und
        // die sind fuer einen Menschen dieselben wie fuer zwanzig.
        var urteile = await Frage([subject], company, cancellationToken);

        return urteile[0];
    }

    /// <summary>Eine Sammelfrage, zwei Paare je Person, in deren Reihenfolge.</summary>
    private async Task<IReadOnlyList<bool>> Frage(
        IReadOnlyList<SubjectId> subject, TenantId company, CancellationToken cancellationToken)
    {
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
            // deshalb laut, nicht als „nicht freigegeben“.
            throw new ConsentGateSilent(
                $"{paare.Length} Paare überschreiten "
                + $"{ConsentLimits.HoechsteSammelgroesse}");
        }

        var response = await Sende(paare, cancellationToken);

        if (response.Results.Count != paare.Length)
        {
            // Eine Antwort, die nicht zu den Fragen passt, lässt sich nicht
            // zuordnen — und falsch zuzuordnen hieße, das PersonProfile der falschen
            // Person zu zeigen. Also weigern statt raten.
            throw new ConsentGateSilent(
                $"{response.Results.Count} Antworten auf {paare.Length} Fragen");
        }

        // Zwei Responses je Person, in der Reihenfolge der Fragen: öffentlich
        // ODER für dieses Unternehmen.
        return [.. Enumerable.Range(0, subject.Count).Select(job =>
            Gilt(response.Results[job * 2]) || Gilt(response.Results[(job * 2) + 1]))];
    }

    /// <summary>
    /// Eine gelöschte Fähigkeit zieht die Erlaubnis logisch zurück.
    /// </summary>
    /// <remarks>
    /// Beides zu prüfen macht die Absicht auch dann richtig, wenn der Ledger
    /// einmal beides zugleich meldet.
    /// </remarks>
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
            protokoll.LogWarning("Der Consent-Ledger ist nicht erreichbar: {Art}",
                error.GetType().Name);

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
                protokoll.LogWarning("Der Consent-Ledger antwortete mit {Status}.",
                    (int)response.StatusCode);

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
