using System.Net;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.Unternehmen;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.Applications.Infrastructure.Anschreiben;

/// <summary>Holt den KI-Zugang der Person aus identity-service.</summary>
/// <remarks>
/// Derselbe interne Draht und dasselbe Geheimnis wie die Mitgliederabfrage.
/// Der Schlüssel reist nur hier, nie durchs Gateway, nie in eine Logzeile.
/// <para>
/// <strong>localhost aus dem Behälter ist die falsche Maschine.</strong>
/// Ollama und MiniMax laufen auf dem Rechner der Person. Im Compose-Stapel
/// ist das <c>host.docker.internal</c> (extra_hosts). Die Oberfläche darf
/// weiter <c>http://localhost:11434/…</c> zeigen — umgeschrieben wird erst
/// hier, und nur im Behälter.
/// </para>
/// </remarks>
public sealed class HttpKiZugang(
    IHttpClientFactory fabrik,
    IOptions<CompanyMemberSettings> identity,
    IOptions<CoverLetterSettings> draft,
    ILogger<HttpKiZugang> protokoll) : IKiZugangAbfrage
{
    public const string Klient = "identity-internal";

    private const string Geheimniskopf = "X-Notify-Secret";

    private readonly CompanyMemberSettings _identity = identity.Value;
    private readonly CoverLetterSettings _entwurf = draft.Value;

    /// <inheritdoc />
    public async Task<KiZugang> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var vomKonto = await FromAccountAsync(subject, cancellationToken);

        if (vomKonto.IstEingerichtet)
        {
            return vomKonto;
        }

        if (_entwurf.Schluessel.Length > 0)
        {
            return new KiZugang(
                "anthropic",
                _entwurf.Adresse,
                _entwurf.Modell,
                _entwurf.Schluessel);
        }

        return vomKonto;
    }

    private async Task<KiZugang> FromAccountAsync(
        SubjectId subject, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_identity.Adresse) || string.IsNullOrEmpty(_identity.Geheimnis))
        {
            return new KiZugang("none", string.Empty, string.Empty, string.Empty);
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _identity.Zeitueberschreitung;

        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_identity.Adresse), $"/internal/account/{subject.Value}/ai"));
            request.Headers.Add(Geheimniskopf, _identity.Geheimnis);

            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            protokoll.LogWarning(
                "Identity-Dienst (KI-Zugang) nicht erreichbar ({Art})", error.GetType().Name);
            throw new CoverLetterUnavailable(
                "identity-service (intern) ist nicht erreichbar.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CoverLetterUnavailable(
                "identity-service (intern) antwortet nicht (Zeitüberschreitung).");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new KiZugang("none", string.Empty, string.Empty, string.Empty);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CoverLetterUnavailable(
                    $"identity-service (intern) antwortet mit {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<KiZugangV1>(cancellationToken)
                ?? throw new CoverLetterUnavailable(
                    "identity-service (intern) sandte eine unbrauchbare Antwort.");

            return new KiZugang(
                body.Provider ?? "none",
                FuerDenBehaelter(Vervollstaendige(body.BaseUrl ?? string.Empty, body.Provider)),
                body.Model ?? string.Empty,
                body.Key ?? string.Empty);
        }
    }

    /// <summary>
    /// Eine nackte Ollama-Adresse ohne Pfad bekommt den OpenAI-Pfad.
    /// </summary>
    public static string Vervollstaendige(string address, string? provider)
    {
        var getrimmt = address.Trim().TrimEnd('/');

        if (provider != "openai_compatible" || getrimmt.Length == 0)
        {
            return getrimmt;
        }

        if (getrimmt.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || getrimmt.Contains("/messages", StringComparison.OrdinalIgnoreCase))
        {
            return getrimmt;
        }

        return $"{getrimmt}/v1/chat/completions";
    }

    /// <summary>
    /// Im Behälter ist localhost der Behälter. Ollama läuft auf der Maschine.
    /// </summary>
    public static string FuerDenBehaelter(string address)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return address;
        }

        return address
            .Replace("://localhost", "://host.docker.internal", StringComparison.OrdinalIgnoreCase)
            .Replace("://127.0.0.1", "://host.docker.internal", StringComparison.Ordinal);
    }
}
