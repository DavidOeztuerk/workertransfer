using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Infrastructure.Sicherheit;

namespace WorkerTransfer.Advisor.Infrastructure.Uebergabe;

/// <summary>Wo transfer-service antwortet.</summary>
public sealed class Uebergabeeinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Transfer";

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
internal sealed record VorgangsantwortV1([property: JsonPropertyName("id")] Guid Id);

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

/// <summary>Macht aus einer Einigung einen Vorgang.</summary>
/// <remarks>
/// <para><strong>Ein Aufruf an die bestehende Tür, und keine Nachbildung.</strong>
/// Der Dreieckskonsens steht in transfer-service: ob der Marktstatus diesem
/// Unternehmen freigegeben ist, ob die Person ansprechbar ist, ob eine Freigabe
/// des jetzigen Arbeitgebers nötig ist. Dieser Adapter kennt keine dieser
/// Regeln, und das ist der Punkt — eine zweite Stelle, die sie kennt, wäre die,
/// die als Erste veraltet.</para>
///
/// <para><strong>Mit dem Token des Aufrufers</strong>, und der Aufrufer ist das
/// Unternehmen. Ein Vorgang beginnt damit, dass ein Unternehmen Interesse
/// zeigt; er kann deshalb gar nicht ohne dessen Token entstehen, und ein
/// Dienstkonto hier hiesse, dass dieser Dienst Vorgänge im Namen von
/// Unternehmen anlegen kann.</para>
/// </remarks>
public sealed class HttpVorgangsuebergabe(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<Uebergabeeinstellungen> einstellungen,
    ILogger<HttpVorgangsuebergabe> protokoll) : IVorgangsuebergabe
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "transfer";

    private readonly Uebergabeeinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Guid> UebergibAsync(
        SubjectId wer, string anlass, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new AuskunftSchweigt("Transfer:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Geduld;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/transfers/"))
        {
            Content = JsonContent.Create(new InteresseV1(wer.Value, anlass ?? string.Empty))
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
            protokoll.LogWarning(
                "transfer-service ist nicht erreichbar: {Art}", fehler.GetType().Name);

            throw new AuskunftSchweigt("transfer-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("transfer-service hat nicht rechtzeitig geantwortet.");

            throw new AuskunftSchweigt("transfer-service timed out");
        }

        using (antwort)
        {
            if (antwort.IsSuccessStatusCode)
            {
                try
                {
                    var gelesen = await antwort.Content
                        .ReadFromJsonAsync<VorgangsantwortV1>(cancellationToken);

                    return gelesen?.Id
                           ?? throw new AuskunftSchweigt("transfer-service sent no id");
                }
                catch (JsonException)
                {
                    throw new AuskunftSchweigt("transfer-service sent an unusable answer");
                }
            }

            // 4xx heisst: transfer-service HAT geantwortet, und zwar ablehnend.
            // Das ist eine Aussage ueber die Bedingungen dieses Vorgangs — etwa
            // ein nicht freigegebener Marktstatus — und keine Stoerung. 5xx ist
            // das Gegenteil, und die beiden duerfen nicht dieselbe Antwort
            // bekommen: „geht nicht" und „wir wissen es nicht" sind zweierlei.
            if ((int)antwort.StatusCode is >= 400 and < 500
                && antwort.StatusCode != HttpStatusCode.RequestTimeout)
            {
                protokoll.LogInformation(
                    "transfer-service hat die Übergabe abgelehnt: {Status}",
                    (int)antwort.StatusCode);

                throw new UebergabeAbgelehnt(
                    $"transfer-service refused with {(int)antwort.StatusCode}");
            }

            protokoll.LogWarning(
                "transfer-service antwortete mit {Status}.", (int)antwort.StatusCode);

            throw new AuskunftSchweigt($"transfer-service returned {(int)antwort.StatusCode}");
        }
    }
}
