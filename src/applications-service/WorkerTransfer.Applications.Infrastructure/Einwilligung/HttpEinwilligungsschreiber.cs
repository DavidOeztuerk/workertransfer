using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Einwilligung;

/// <summary>Wie eine Erteilung auf der Leitung aussieht.</summary>
internal sealed record Erteilung(Guid SubjectId, string Capability);

/// <summary>Wie ein Revocation aussieht.</summary>
/// <remarks>
/// Der Ledger verlangt für einen Revocation eine Begründung und für eine
/// Erteilung keine: etwas wegzunehmen muss erklärbar sein, es zu geben nicht.
/// Die hier sagt ehrlich, wodurch der Revocation ausgelöst wurde, statt eine zu
/// erfinden.
/// </remarks>
internal sealed record Revocation(Guid SubjectId, string Capability, string Reason);

/// <summary>Schreibt in den Ledger.</summary>
/// <remarks>
/// Jeder Aufruf reist mit dem Token <em>des Aufrufers</em> statt mit einem
/// Dienstkonto, damit das Protokoll des Ledgers festhält, wer wirklich gefragt
/// hat. Ein fremder Akteur wird dort abgewiesen — was richtig ist: die
/// Release erteilt die Person, nicht dieser Dienst.
/// </remarks>
public sealed class HttpConsentWriter(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<ConsentSettings> einstellungen,
    ILogger<HttpConsentWriter> protokoll) : IConsentWriter
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>Snake_case in beide Richtungen — die Form, die der Ledger spricht.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Begruendung = "Bewerbung zurückgezogen";

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task GrantAsync(
        SubjectId subject,
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skills);

        foreach (var skill in skills)
        {
            await WriteAsync(
                "/consent/grant", new Erteilung(subject.Value, skill), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        SubjectId subject,
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skills);

        foreach (var skill in skills)
        {
            await WriteAsync(
                "/consent/revoke",
                new Revocation(subject.Value, skill, Begruendung),
                cancellationToken);
        }
    }

    private async Task WriteAsync<TRumpf>(
        string path,
        TRumpf body,
        CancellationToken cancellationToken)
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
        }
    }
}
