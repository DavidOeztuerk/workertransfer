using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Hit;

namespace WorkerTransfer.Scout.Infrastructure.Belege;

/// <summary>Wo die Belege liegen.</summary>
public sealed class EvidenceSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "GitHub";

    /// <summary>Die Basisadresse von github-service.</summary>
    /// <remarks>
    /// <strong>Nicht</strong> die von github.com. Dieser Dienst fragt nie bei
    /// GitHub selbst — das tut github-service, auf Knopfdruck einer Person und
    /// nie im Hintergrund (ADR-0004, ADR-0033). Die Adresse steht in der
    /// Konfiguration, weil die Egress-Grenze ihre erlaubten Hosts von dort
    /// ableitet.
    /// </remarks>
    public string Adresse { get; set; } = "http://github-service:8011";

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Wie viele Belege ein Hit höchstens trägt.</summary>
    /// <remarks>
    /// Ein Deckel, damit eine Trefferliste nicht zur Repositoryliste wird — und
    /// ausdrücklich keine <em>Auswahl</em> nach Güte: gekürzt wird in der
    /// Reihenfolge, in der github-service liefert. Wer nach Sternen
    /// aussortierte, hätte eine Rangfolge gebaut (ADR-0022).
    /// </remarks>
    public int HoechstzahlBelege { get; set; } = 24;
}

/// <summary>Holt Belege zu einem Hit — nie, um ihn zu finden.</summary>
/// <remarks>
/// <para><c>GET /github/{subjectId}</c> prüft die Release
/// (<c>github.visibility:public</c>) und den Nachweis selbst: eine bloss
/// genannte Connection gibt dieser Dienst nicht heraus, sonst zeigte jemand
/// die Repositories eines fremden Kontos als seine (ADR-0033). Gefragt wird im
/// Namen des Aufrufers, damit dort dieselbe Entscheidung fällt wie überall.</para>
///
/// <para><strong>Ein Fehlschlag ist kein Fehlschlag der Search.</strong> Diese
/// Klasse wirft nicht: der Hit bleibt stehen, und sein
/// <see cref="Belegstand"/> sagt, was fehlt. Ein 404 heisst „nichts
/// freigegeben" und ist <em>kein Urteil</em>; ein Ausfall heisst
/// „unerreichbar", und das ist wieder etwas anderes. Die beiden gleich zu
/// benennen wäre eine Aussage über einen Menschen, die aus unserem Ausfall
/// stammt (ADR-0022 §3).</para>
/// </remarks>
public sealed class HttpEvidenceSource(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<EvidenceSettings> einstellungen,
    ILogger<HttpEvidenceSource> protokoll) : IEvidenceSource
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "github";

    private readonly EvidenceSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<EvidenceSheet> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            return new EvidenceSheet([], EvidenceState.Unerreichbar);
        }

        ForeignConnectionV1? connection;

        try
        {
            connection = await Hole(subject, cancellationToken);
        }
        catch (Exception error)
            when (error is HttpRequestException or TaskCanceledException
                  or NotSupportedException or JsonException)
        {
            // Die Art, nie der Inhalt.
            //
            // `JsonException` steht hier ausdruecklich dabei: eine unlesbare
            // Antwort ist derselbe Fall wie keine Antwort — und ohne diesen
            // Zweig verliesse sie den Dienst als 500 und riesse die GANZE
            // HitPage mit, obwohl nur ein Evidence fehlt. Ein Fehlschlag beim
            // Evidence ist kein Fehlschlag der Search.
            protokoll.LogWarning(
                "github-service ist nicht erreichbar: {Art}", error.GetType().Name);

            return new EvidenceSheet([], EvidenceState.Unerreichbar);
        }

        if (connection is null)
        {
            // 404: nicht vorhanden, nicht bewiesen, nicht freigegeben — und die
            // drei antworten dort mit Absicht gleich. Hier heisst das schlicht:
            // es liegt nichts vor. Nicht: es ist nichts da.
            return new EvidenceSheet([], EvidenceState.KeineFreigegeben);
        }

        var evidences = (connection.Repositories ?? [])
            .SelectMany(Aus)
            // Entdoppelt ueber Wort UND Projekt: dasselbe Topic an zwei
            // Repositories sind zwei Belege, an einem Repository einer.
            .DistinctBy(evidence => (evidence.Wort, evidence.Projekt, evidence.Art))
            .Take(Math.Max(1, _einstellungen.HoechstzahlBelege))
            .ToArray();

        if (evidences.Length == 0)
        {
            return new EvidenceSheet([], EvidenceState.KeineFreigegeben);
        }

        return new EvidenceSheet(
            evidences,
            connection.LanguagesComplete ? EvidenceState.Vollstaendig : EvidenceState.Unvollstaendig);
    }

    /// <summary>Topics zuerst, Sprachen danach.</summary>
    /// <remarks>
    /// <para><strong>Die Reihenfolge ordnet Worte, nie Menschen</strong>
    /// (ADR-0033). Ein Topic ist eine <em>Nennung</em> — ein Mensch hat
    /// „kubernetes" an dieses Repository geschrieben; ein Sprachname ist
    /// GitHubs Erkennung an Dateien. Das Genannte steht vorn, und aus dieser
    /// Reihenfolge folgt über niemanden ein Rang.</para>
    ///
    /// <para>None Byte-Zahl, kein Anteil, keine Sterne: sie kommen hier gar
    /// nicht erst an.</para>
    /// </remarks>
    private static IEnumerable<Evidence> Aus(FremdrepositoryV1 repository)
    {
        foreach (var thema in repository.Topics ?? [])
        {
            yield return new Evidence(thema, EvidenceKind.Thema, repository.Name, repository.Url);
        }

        foreach (var language in repository.Languages ?? [])
        {
            yield return new Evidence(language, EvidenceKind.Sprache, repository.Name, repository.Url);
        }
    }

    private async Task<ForeignConnectionV1?> Hole(SubjectId subject, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(_einstellungen.Adresse), $"/github/{subject.Value}"));

        // Im Namen des Aufrufers: github-service prueft die Release selbst,
        // und es soll dieselbe Entscheidung faellen wie fuer einen Browser.
        if (Aufrufertoken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            protokoll.LogWarning(
                "github-service antwortete mit {Status}.", (int)response.StatusCode);

            throw new HttpRequestException("github-service returned an error status");
        }

        return await response.Content.ReadFromJsonAsync<ForeignConnectionV1>(cancellationToken);
    }

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
