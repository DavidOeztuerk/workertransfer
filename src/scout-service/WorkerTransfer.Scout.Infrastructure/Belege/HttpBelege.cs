using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Treffer;

namespace WorkerTransfer.Scout.Infrastructure.Belege;

/// <summary>Wo die Belege liegen.</summary>
public sealed class Belegeinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "GitHub";

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

    /// <summary>Wie viele Belege ein Treffer höchstens trägt.</summary>
    /// <remarks>
    /// Ein Deckel, damit eine Trefferliste nicht zur Repositoryliste wird — und
    /// ausdrücklich keine <em>Auswahl</em> nach Güte: gekürzt wird in der
    /// Reihenfolge, in der github-service liefert. Wer nach Sternen
    /// aussortierte, hätte eine Rangfolge gebaut (ADR-0022).
    /// </remarks>
    public int HoechstzahlBelege { get; set; } = 24;
}

/// <summary>Holt Belege zu einem Treffer — nie, um ihn zu finden.</summary>
/// <remarks>
/// <para><c>GET /github/{subjectId}</c> prüft die Freigabe
/// (<c>github.visibility:public</c>) und den Nachweis selbst: eine bloss
/// genannte Verbindung gibt dieser Dienst nicht heraus, sonst zeigte jemand
/// die Repositories eines fremden Kontos als seine (ADR-0033). Gefragt wird im
/// Namen des Aufrufers, damit dort dieselbe Entscheidung fällt wie überall.</para>
///
/// <para><strong>Ein Fehlschlag ist kein Fehlschlag der Suche.</strong> Diese
/// Klasse wirft nicht: der Treffer bleibt stehen, und sein
/// <see cref="Belegstand"/> sagt, was fehlt. Ein 404 heisst „nichts
/// freigegeben" und ist <em>kein Urteil</em>; ein Ausfall heisst
/// „unerreichbar", und das ist wieder etwas anderes. Die beiden gleich zu
/// benennen wäre eine Aussage über einen Menschen, die aus unserem Ausfall
/// stammt (ADR-0022 §3).</para>
/// </remarks>
public sealed class HttpBelege(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<Belegeinstellungen> einstellungen,
    ILogger<HttpBelege> protokoll) : IBelege
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "github";

    private readonly Belegeinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<Belegbogen> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            return new Belegbogen([], Belegstand.Unerreichbar);
        }

        FremdverbindungV1? verbindung;

        try
        {
            verbindung = await Hole(wer, cancellationToken);
        }
        catch (Exception fehler)
            when (fehler is HttpRequestException or TaskCanceledException
                  or NotSupportedException or JsonException)
        {
            // Die Art, nie der Inhalt.
            //
            // `JsonException` steht hier ausdruecklich dabei: eine unlesbare
            // Antwort ist derselbe Fall wie keine Antwort — und ohne diesen
            // Zweig verliesse sie den Dienst als 500 und riesse die GANZE
            // Trefferseite mit, obwohl nur ein Beleg fehlt. Ein Fehlschlag beim
            // Beleg ist kein Fehlschlag der Suche.
            protokoll.LogWarning(
                "github-service ist nicht erreichbar: {Art}", fehler.GetType().Name);

            return new Belegbogen([], Belegstand.Unerreichbar);
        }

        if (verbindung is null)
        {
            // 404: nicht vorhanden, nicht bewiesen, nicht freigegeben — und die
            // drei antworten dort mit Absicht gleich. Hier heisst das schlicht:
            // es liegt nichts vor. Nicht: es ist nichts da.
            return new Belegbogen([], Belegstand.KeineFreigegeben);
        }

        var belege = (verbindung.Repositories ?? [])
            .SelectMany(Aus)
            // Entdoppelt ueber Wort UND Projekt: dasselbe Topic an zwei
            // Repositories sind zwei Belege, an einem Repository einer.
            .DistinctBy(beleg => (beleg.Wort, beleg.Projekt, beleg.Art))
            .Take(Math.Max(1, _einstellungen.HoechstzahlBelege))
            .ToArray();

        if (belege.Length == 0)
        {
            return new Belegbogen([], Belegstand.KeineFreigegeben);
        }

        return new Belegbogen(
            belege,
            verbindung.LanguagesComplete ? Belegstand.Vollstaendig : Belegstand.Unvollstaendig);
    }

    /// <summary>Topics zuerst, Sprachen danach.</summary>
    /// <remarks>
    /// <para><strong>Die Reihenfolge ordnet Worte, nie Menschen</strong>
    /// (ADR-0033). Ein Topic ist eine <em>Nennung</em> — ein Mensch hat
    /// „kubernetes" an dieses Repository geschrieben; ein Sprachname ist
    /// GitHubs Erkennung an Dateien. Das Genannte steht vorn, und aus dieser
    /// Reihenfolge folgt über niemanden ein Rang.</para>
    ///
    /// <para>Keine Byte-Zahl, kein Anteil, keine Sterne: sie kommen hier gar
    /// nicht erst an.</para>
    /// </remarks>
    private static IEnumerable<Beleg> Aus(FremdrepositoryV1 repository)
    {
        foreach (var thema in repository.Topics ?? [])
        {
            yield return new Beleg(thema, Belegart.Thema, repository.Name, repository.Url);
        }

        foreach (var sprache in repository.Languages ?? [])
        {
            yield return new Beleg(sprache, Belegart.Sprache, repository.Name, repository.Url);
        }
    }

    private async Task<FremdverbindungV1?> Hole(SubjectId wer, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(_einstellungen.Adresse), $"/github/{wer.Value}"));

        // Im Namen des Aufrufers: github-service prueft die Freigabe selbst,
        // und es soll dieselbe Entscheidung faellen wie fuer einen Browser.
        if (Aufrufertoken() is { Length: > 0 } token)
        {
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var antwort = await client.SendAsync(anfrage, cancellationToken);

        if (antwort.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        if (!antwort.IsSuccessStatusCode)
        {
            protokoll.LogWarning(
                "github-service antwortete mit {Status}.", (int)antwort.StatusCode);

            throw new HttpRequestException("github-service returned an error status");
        }

        return await antwort.Content.ReadFromJsonAsync<FremdverbindungV1>(cancellationToken);
    }

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
