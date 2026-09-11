using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Infrastructure.Sicherheit;
using WorkerTransfer.Contracts.Consent;

namespace WorkerTransfer.Advisor.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger erreichbar ist.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    /// <remarks>
    /// Im Compose-Netz der Dienstname, nicht <c>localhost</c> — der Aufruf läuft
    /// von Behälter zu Behälter. Und er steht in der Konfiguration, weil die
    /// Egress-Grenze ihre erlaubten Hosts von dort ableitet
    /// (<c>Dienstgrundlage.GerufeneHosts</c>): ein Ziel, das nur im Quelltext
    /// stünde, wird abgewiesen — ohne Protokollzeile.
    /// </remarks>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fünf Sekunden. OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>:
    /// HUNDERT Sekunden — das ist kein Zeitlimit, sondern ein hängender Aufruf
    /// mit einem Ende irgendwann, und er hängt den Aufrufer mit.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Wie eine Erteilung auf der Leitung aussieht.</summary>
internal sealed record Erteilung(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability);

/// <summary>Wie ein Widerruf aussieht.</summary>
/// <remarks>
/// Der Ledger verlangt für einen Widerruf eine Begründung und für eine
/// Erteilung keine: etwas wegzunehmen muss erklärbar sein, es zu geben nicht.
/// </remarks>
internal sealed record Widerruf(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// Der Ledger — gelesen und beschrieben, beides synchron und ohne
/// Zwischenspeicher.
/// </summary>
/// <remarks>
/// <para>Es gibt hier kein Feld, das eine Antwort halten könnte, und keinen
/// <c>IMemoryCache</c> im Konstruktor. In einem Dienst, dessen ganzer Zweck
/// gestufte Sichtbarkeit ist, wäre ein Zwischenspeicher kein Leistungsdetail,
/// sondern der Bruch der Zusage: ein Widerruf muss bei der nächsten Anfrage
/// wirken (ADR-0013).</para>
///
/// <para><strong>Wessen Subjekt gefragt oder geschrieben wird, steht nie im
/// Rumpf dieses Dienstes — es steht im Token.</strong> Erteilen und Widerrufen
/// schicken die Kennung, die im geprüften Token des Aufrufers steht; der Ledger
/// vergleicht sie selbst und antwortet 403 „a consent belongs to its subject",
/// wenn sie nicht passt. Deshalb kann eine Stufenfreigabe nur gelingen, wenn
/// die Person sie auslöst.</para>
/// </remarks>
public sealed class HttpEinwilligungstor(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    ICurrentPrincipal akteur,
    IOptions<Einwilligungseinstellungen> einstellungen,
    ILogger<HttpEinwilligungstor> protokoll) : IEinwilligungstor
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>
    /// Wie viele Paare eine Runde trägt.
    /// </summary>
    /// <remarks>
    /// Abgeleitet, nicht gewählt: je Paar werden vier Fähigkeiten gefragt, und
    /// der Ledger deckelt eine Sammelfrage bei hundert Paaren. Vier mal
    /// fünfundzwanzig ist hundert.
    /// </remarks>
    public const int PaareJeRunde = Einwilligungsgrenzen.HoechsteSammelgroesse / 4;

    private readonly Einwilligungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Stufe>> StufenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        if (paare.Count == 0)
        {
            return [];
        }

        var stufen = new List<Stufe>(paare.Count);

        // Gestueckelt, weil der Ledger eine Sammelfrage deckelt. Mehrere Wege
        // hin und zurueck sind immer noch einer je fuenfundzwanzig Zeilen statt
        // vier je Zeile.
        foreach (var stapel in paare.Chunk(PaareJeRunde))
        {
            var fragen = stapel
                .SelectMany(paar => Faehigkeiten(paar.Firma)
                    .Select(faehigkeit => new EinwilligungsfrageV1(paar.Wer.Value, faehigkeit)))
                .ToArray();

            var antwort = await SchickeAsync<EinwilligungssammelfrageV1,
                EinwilligungssammelantwortV1>(
                "/consent/check-batch",
                new EinwilligungssammelfrageV1(fragen),
                cancellationToken);

            // Die Reihenfolge gehoert zum Vertrag, und die Laenge auch. Eine
            // unpassende Antwort wird abgewiesen statt geraten: sie falsch
            // zuzuordnen hiesse, die Freigabe des falschen Menschen zu lesen.
            if (antwort.Results.Count != fragen.Length)
            {
                throw new EinwilligungSchweigt(
                    $"{antwort.Results.Count} Antworten auf {fragen.Length} Fragen");
            }

            for (var i = 0; i < stapel.Length; i++)
            {
                stufen.Add(Lies(antwort.Results, i * Anzahl));
            }
        }

        return stufen;
    }

    /// <inheritdoc />
    public async Task ErteileAsync(
        IReadOnlyList<string> faehigkeiten, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faehigkeiten);

        var wer = Selbst();

        foreach (var faehigkeit in faehigkeiten)
        {
            await SchickeAsync<Erteilung, JsonElement>(
                "/consent/grant", new Erteilung(wer, faehigkeit), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task WiderrufeAsync(
        IReadOnlyList<string> faehigkeiten,
        string grund,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(faehigkeiten);

        var wer = Selbst();

        foreach (var faehigkeit in faehigkeiten)
        {
            await SchickeAsync<Widerruf, JsonElement>(
                "/consent/revoke", new Widerruf(wer, faehigkeit, grund), cancellationToken);
        }
    }

    /// <summary>Die vier Fragen je Paar, in fester Reihenfolge.</summary>
    /// <remarks>
    /// Die Reihenfolge ist der Vertrag zwischen dieser Methode und
    /// <see cref="Lies"/>; sie zu ändern, ohne dort zu zählen, hiesse, die
    /// Antwort auf „Lebenslauf" als „Profil" zu lesen.
    /// </remarks>
    private static string[] Faehigkeiten(TenantId firma) =>
    [
        Stufenfaehigkeiten.Profil(firma),
        Stufenfaehigkeiten.ProfilOeffentlich,
        Stufenfaehigkeiten.Lebenslauf(firma),
        Stufenfaehigkeiten.Klarname(firma)
    ];

    private const int Anzahl = 4;

    /// <summary>Aus vier Antworten eine Stufe — kumulativ.</summary>
    /// <remarks>
    /// Eine Stufe ist ein <em>Stand</em> und kein Sprung: wer Stufe 1
    /// zurückgenommen hat, steht nicht auf 3, auch wenn die Fähigkeit von
    /// Stufe 3 noch steht. Der Lebenslauf sichtbar und das Profil nicht wäre
    /// eine Erlaubnis ohne Grundlage.
    /// </remarks>
    private static Stufe Lies(IReadOnlyList<EinwilligungsantwortV1> antworten, int ab)
    {
        var profil = Gilt(antworten[ab]) || Gilt(antworten[ab + 1]);
        var lebenslauf = Gilt(antworten[ab + 2]);
        var klarname = Gilt(antworten[ab + 3]);

        if (!profil)
        {
            return Stufe.Keine;
        }

        if (!lebenslauf)
        {
            return Stufe.Profil;
        }

        return klarname ? Stufe.Person : Stufe.Unterlagen;
    }

    /// <summary>Eine gelöschte Fähigkeit zieht die Erlaubnis logisch zurück.</summary>
    private static bool Gilt(EinwilligungsantwortV1 antwort) =>
        antwort.Granted && !antwort.Deleted;

    /// <summary>Wer gerade handelt — aus dem geprüften Token, nie aus dem Rumpf.</summary>
    private Guid Selbst() =>
        akteur.Current is { } handelnder
            ? handelnder.Subject.Value
            : throw new EinwilligungAbgelehnt("not authenticated");

    private async Task<TAntwort> SchickeAsync<TFrage, TAntwort>(
        string pfad, TFrage rumpf, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            // Kein Ledger eingerichtet heisst nicht „alles frei". Es heisst,
            // dass dieser Dienst die Frage nicht beantworten kann.
            throw new EinwilligungSchweigt("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Geduld;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), pfad))
        {
            Content = JsonContent.Create(rumpf)
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
            // Nur die Art, nie der Inhalt: gefragt wurde nach einem Menschen.
            protokoll.LogWarning(
                "Der Consent-Ledger ist nicht erreichbar: {Art}", fehler.GetType().Name);

            throw new EinwilligungSchweigt("consent-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Der Consent-Ledger hat nicht rechtzeitig geantwortet.");

            throw new EinwilligungSchweigt("consent-service timed out");
        }

        using (antwort)
        {
            if (antwort.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                // Der Ledger HAT geantwortet, und zwar mit Nein — das ist etwas
                // anderes als sein Schweigen. Es passiert genau dann, wenn
                // jemand fuer einen fremden Menschen erteilen wollte.
                protokoll.LogWarning(
                    "Der Consent-Ledger hat abgelehnt: {Status}", (int)antwort.StatusCode);

                throw new EinwilligungAbgelehnt(
                    $"consent-service refused with {(int)antwort.StatusCode}");
            }

            if (!antwort.IsSuccessStatusCode)
            {
                protokoll.LogWarning(
                    "Der Consent-Ledger antwortete mit {Status}.", (int)antwort.StatusCode);

                throw new EinwilligungSchweigt(
                    $"consent-service returned {(int)antwort.StatusCode}");
            }

            try
            {
                return await antwort.Content
                           .ReadFromJsonAsync<TAntwort>(cancellationToken)
                       ?? throw new EinwilligungSchweigt("consent-service sent an empty answer");
            }
            catch (Exception fehler) when (fehler is not EinwilligungSchweigt)
            {
                protokoll.LogWarning("Der Consent-Ledger antwortete unverständlich.");

                throw new EinwilligungSchweigt("consent-service sent an unusable answer");
            }
        }
    }
}
