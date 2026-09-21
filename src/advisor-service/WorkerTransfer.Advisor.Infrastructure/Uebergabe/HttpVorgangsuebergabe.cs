using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Infrastructure.Sicherheit;

namespace WorkerTransfer.Advisor.Infrastructure.Uebergabe;

/// <summary>Wo transfer-service antwortet.</summary>
public sealed class HandoverSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Transfer";

    /// <summary>Die Basisadresse von transfer-service.</summary>
    /// <remarks>
    /// In der Konfiguration, weil die Egress-Grenze ihre erlaubten Hosts von
    /// dort ableitet. Ohne diese Zeile wird die Übergabe abgewiesen — ohne
    /// Protokollzeile, und der Ausfall sähe aus wie einer von transfer-service.
    /// </remarks>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Was transfer-service beim Anlegen zurückgibt — nur die Kennung interessiert.</summary>
internal sealed record CaseResponseV1([property: JsonPropertyName("id")] Guid Id);

/// <summary>Was hinausgeht, um Interesse zu zeigen.</summary>
/// <remarks>
/// Ein <em>typisierter</em> Vertrag und kein anonymes Objekt. <c>new { subjectId
/// = … }</c> kompiliert, serialisiert und kommt am Empfänger, der
/// <c>[JsonPropertyName("subject_id")]</c> deklariert, als <c>Guid.Empty</c> an
/// — gemessen an vier Benachrichtigungssprüngen, die deshalb nie angekommen
/// sind.
/// </remarks>
internal sealed record InteresseV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("message")] string Message);

/// <summary>Macht aus einer Einigung einen Case.</summary>
/// <remarks>
/// <para><strong>Ein Aufruf an die bestehende Tür, und keine Nachbildung.</strong>
/// Der Dreieckskonsens steht in transfer-service: ob der MarketStatus diesem
/// Unternehmen freigegeben ist, ob die Person ansprechbar ist, ob eine Release
/// des jetzigen Arbeitgebers nötig ist. Dieser Adapter kennt keine dieser
/// Regeln, und das ist der Punkt — eine zweite Job, die sie kennt, wäre die,
/// die als Erste veraltet.</para>
///
/// <para><strong>Mit dem Token des Aufrufers</strong>, und der Aufrufer ist das
/// Unternehmen. Ein Case beginnt damit, dass ein Unternehmen Interesse
/// zeigt; er kann deshalb gar nicht ohne dessen Token entstehen, und ein
/// Dienstkonto hier hiesse, dass dieser Dienst Vorgänge im Namen von
/// Unternehmen anlegen kann.</para>
/// </remarks>
public sealed class HttpCaseHandover(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<HandoverSettings> einstellungen,
    ILogger<HttpCaseHandover> protokoll) : ICaseHandover
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "transfer";

    private readonly HandoverSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Guid> HandOverAsync(
        SubjectId subject, string anlass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new LookupSilent("Transfer:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/transfers/"))
        {
            Content = JsonContent.Create(new InteresseV1(subject.Value, anlass ?? string.Empty))
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
            protokoll.LogWarning(
                "transfer-service ist nicht erreichbar: {Art}", error.GetType().Name);

            throw new LookupSilent("transfer-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("transfer-service hat nicht rechtzeitig geantwortet.");

            throw new LookupSilent("transfer-service timed out");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var gelesen = await response.Content
                        .ReadFromJsonAsync<CaseResponseV1>(cancellationToken);

                    return gelesen?.Id
                           ?? throw new LookupSilent("transfer-service sent no id");
                }
                catch (JsonException)
                {
                    throw new LookupSilent("transfer-service sent an unusable answer");
                }
            }

            // 4xx heisst: transfer-service HAT geantwortet, und zwar ablehnend.
            // Das ist eine Aussage ueber die Bedingungen dieses Vorgangs — etwa
            // ein nicht freigegebener MarketStatus — und keine Stoerung. 5xx ist
            // das Gegenteil, und die beiden duerfen nicht dieselbe Antwort
            // bekommen: „geht nicht" und „wir wissen es nicht" sind zweierlei.
            if ((int)response.StatusCode is >= 400 and < 500
                && response.StatusCode != HttpStatusCode.RequestTimeout)
            {
                protokoll.LogInformation(
                    "transfer-service hat die Übergabe abgelehnt: {Status}",
                    (int)response.StatusCode);

                throw new HandoverRefused(
                    $"transfer-service refused with {(int)response.StatusCode}");
            }

            protokoll.LogWarning(
                "transfer-service antwortete mit {Status}.", (int)response.StatusCode);

            throw new LookupSilent($"transfer-service returned {(int)response.StatusCode}");
        }
    }
}
