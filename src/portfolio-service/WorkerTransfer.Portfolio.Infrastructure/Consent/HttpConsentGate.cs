using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Portfolio.Application.Ports;

namespace WorkerTransfer.Portfolio.Infrastructure.Consent;

/// <summary>Where the ledger stands.</summary>
public sealed class ConsentSettings
{
    /// <summary>The section in the configuration.</summary>
    public const string Section = "Consent";

    /// <summary>The base address of consent-service.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Consent__Adresse</c>), so
    /// it stays German until the keys are renamed everywhere at once — in
    /// compose, in the chart and in <c>.env.example</c>.
    /// </remarks>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>How long an answer is waited for.</summary>
    /// <remarks>
    /// Five seconds, as at every other service-to-service call here. WITHOUT
    /// this line the default of <c>HttpClient</c> applies: ONE HUNDRED seconds.
    /// That is no time limit, that is a hanging call with an end some time —
    /// and it hangs the caller with it.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks the ledger — synchronously, every time, without a cache.</summary>
/// <remarks>
/// The capability is <c>portfolio.visibility:public</c>, and it covers the
/// attachments too (ADR-0021). A second gate for files would be a second
/// truth, and a revocation would then take effect on the description but not on
/// the work sample.
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    IHttpContextAccessor accessor,
    IOptions<ConsentSettings> settings) : IConsentGate
{
    /// <summary>The one capability this is about.</summary>
    public const string Capability = "portfolio.visibility:public";

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            // No ledger configured does not mean "everything is free". It
            // means this service cannot answer the question.
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = factory.CreateClient(nameof(HttpConsentGate));

        client.Timeout = _settings.Zeitueberschreitung;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.Adresse), "/consent/check"))
        {
            Content = JsonContent.Create(new ConsentQuestionV1(subject.Value, Capability))
        };

        // On behalf of the caller, not with a service account: the ledger's own
        // log should record who really asked.
        if (CallerToken() is { } token)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // The kind, never the content — and expressly not a `false`: "I do
            // not know" is something different from "no".
            throw new ConsentGateSilent(
                $"Der Consent-Ledger antwortet nicht ({error.GetType().Name}).");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ConsentGateSilent(
                    $"Der Consent-Ledger antwortet mit {(int)response.StatusCode}.");
            }

            using var read = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return read.RootElement.TryGetProperty("granted", out var granted)
                   && granted.GetBoolean();
        }
    }

    private string? CallerToken()
    {
        var request = accessor.HttpContext?.Request;

        if (request is null)
        {
            return null;
        }

        var header = request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..]
            : request.Cookies.TryGetValue("access", out var cookie) && cookie.Length > 0
                ? cookie
                : null;
    }
}
