using System.Globalization;
using System.Net;
using System.Text.Json;
using Girder.Abstractions.Caching;

namespace WorkerTransfer.Gateway;

/// <summary>
/// Wie oft eine Herkunft einen Pfad je Fenster aufrufen darf.
/// </summary>
/// <remarks>
/// Kommt aus <c>ocelot.json</c>, Abschnitt <c>Bremse</c> — derselben Datei wie
/// die Routen. Nicht aus einer zweiten: die gebremsten Pfade sind eine Auswahl
/// <em>aus</em> der Landkarte, und zwei Dateien über dieselben Pfade gehen beim
/// ersten neuen Endpunkt auseinander. <c>BremsenkarteTests</c> nagelt fest, dass
/// jeder gebremste Pfad wirklich eine Route hat.
/// </remarks>
public sealed class Bremseinstellungen
{
    /// <summary>Der Abschnitt in <c>ocelot.json</c>.</summary>
    public const string Abschnitt = "Bremse";

    /// <summary>Das Fenster, über das gezählt wird.</summary>
    public TimeSpan Fenster { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Pfad → erlaubte Aufrufe je Fenster und Herkunft.</summary>
    public IDictionary<string, int> Pfade { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Luft für Umgebungen, in denen ein Prüfstand sich selbst hämmert.
    /// </summary>
    /// <remarks>
    /// <strong>1 ist die Voreinstellung und der ausgelieferte Wert.</strong> Die
    /// Zahlen in <c>ocelot.json</c> gelten also, wo nichts anderes gesagt wird —
    /// <c>Die_ausgelieferte_Karte_multipliziert_nichts</c> nagelt das fest.
    /// <para>
    /// Gebraucht wird das für genau einen Fall: die Playwright-Reisen legen
    /// <strong>34 Konten</strong> an, alle von einer Herkunft und binnen
    /// weniger Minuten. Gegen <c>/auth/register: 5</c> fällt das um, und zwar
    /// zu Recht — kein Mensch legt vierunddreißig Konten an. Ein Prüfstand ist
    /// nicht die Bedrohung, gegen die diese Bremse gebaut ist.
    /// </para>
    /// <para>
    /// Ein Faktor und kein Schalter, aus einem Grund: die Bremse LÄUFT dann
    /// weiter — sie zählt, sie schlüsselt je Herkunft, sie antwortet mit ihren
    /// Kopfzeilen. Nur die Decke liegt höher. Eine ausgeschaltete Bremse wäre
    /// in der einzigen laufenden Umgebung gar nicht mehr zu sehen, und was man
    /// nie sieht, merkt man auch nicht, wenn es kaputtgeht.
    /// </para>
    /// <para>
    /// <strong>Er gehört nach docker-compose.yml und nirgendwo sonst.</strong>
    /// Nicht ins Chart: dort läuft kein Prüfstand, und ein Faktor in einer
    /// Staging-Umgebung wäre eine Bremse, die nur so aussieht.
    /// </para>
    /// </remarks>
    public int Faktor { get; init; } = 1;
}

/// <summary>
/// Die Bremse an den Auth-Endpunkten: je Herkunft, nie je E-Mail-Adresse.
/// </summary>
/// <remarks>
/// <para><strong>Warum sie hier sitzt und nicht in identity-service.</strong>
/// Gemessen, nicht gewählt: Girders Zähler und jede andere Bremse brauchen die
/// <em>Herkunft</em> des Aufrufers, und die steht in
/// <c>Connection.RemoteIpAddress</c>. Hinter dem Gateway ist das die Adresse
/// <em>des Gateways</em> — für jeden Aufrufer dieselbe. Eine Bremse in
/// identity-service hätte also alle Menschen in einen Topf geworfen: wer als
/// Erster fünfmal danebentippt, sperrt die ganze Welt aus. Das ist keine Bremse,
/// das ist ein Selbstangriff.</para>
///
/// <para>Der übliche Ausweg — <c>X-Forwarded-For</c> — wäre hier schlimmer als
/// das Problem. Diesen Kopf setzt jeder Aufrufer selbst; ihm zu glauben hieße,
/// dem Angreifer den Schlüssel des Zählers in die Hand zu geben. Er dreht ihn
/// bei jeder Anfrage und ist nie gebremst. Ein weitergereichter Kopf ist erst
/// dann etwas wert, wenn eine <em>vertrauenswürdige</em> Kette ihn setzt, und
/// die gibt es hier nicht: in Compose sind die Dienste unter 8001–8011 direkt
/// erreichbar. Also wird er nicht gelesen.</para>
///
/// <para>Und in <c>ServiceDefaults</c> gehört sie erst recht nicht: dort steht,
/// was für alle elf Dienste gleich sein <em>muss</em>. Eine Bremse ist eine
/// Entscheidung über bestimmte Endpunkte, und genau einer hat sie.</para>
///
/// <para><strong>Je Herkunft, nie je Adresse.</strong> Der Schlüssel besteht aus
/// Pfad und Herkunft, sonst nichts — der Rumpf wird nicht angefasst. Wer je
/// E-Mail-Adresse bremste, baute genau den Aufzählungskanal, den
/// <c>/auth/register</c> schließt: eine gebremste Antwort verriete, dass die
/// Adresse existiert. Er ließe außerdem einen Fremden jeden Menschen aussperren,
/// dessen Adresse er kennt.</para>
///
/// <para><strong>Weiter außen als die Authentifizierung.</strong> Hier ist noch
/// kein Token geprüft und kein Passwort gehasht. Säße die Bremse hinter bcrypt,
/// kostete jeder Versuch einen Hash und die Bremse wäre selbst der teuerste Teil
/// des Angriffs.</para>
///
/// <para><strong>Der Zähler läuft im Prozess</strong> (<c>InMemoryRateLimitStore</c>).
/// Bei zwei Gateway-Instanzen hat jede ihr eigenes Fenster und die wirksame
/// Grenze verdoppelt sich — deshalb hängt sie an <c>replicaCount: 1</c>. Der
/// Ausweg ist ein Registrierungswechsel, kein Umbau:
/// <c>RedisDistributedRateLimitStore</c> erfüllt dieselbe Schnittstelle.</para>
///
/// <para><strong>Warum nicht Girders Bremse.</strong> Der alte Grund ist weg:
/// Girder hatte drei Ratenbegrenzungen, zwei bremsten nicht, die dritte war
/// nirgends verdrahtet, und alle drei glaubten <c>X-Forwarded-For</c> ohne jede
/// Vertrauensliste. Das ist seit 4.0.0 behoben und in H2 nachgemessen: es bleibt
/// eine, sie bremst, sie zählt je Herkunft, sie liest den Kopf nicht mehr, und
/// ein gefälschtes <c>X-Forwarded-For: 127.0.0.1</c> hebt sie nicht auf — obwohl
/// die Ausnahmeliste weiterhin Loopback trägt, denn die Herkunft kommt jetzt
/// allein aus <c>Connection.RemoteIpAddress</c>. Je-Pfad-Grenzen kann sie auch.</para>
///
/// <para>Was sie <em>nicht</em> kann, ist der Grund, warum diese Kette bleibt:
/// <strong>ihre Abweisung ist kein Problemdokument und trägt keine
/// Korrelationskennung.</strong> Sie schreibt <c>application/json</c> mit einem
/// <c>traceId</c> aus <c>HttpContext.TraceIdentifier</c>, fest verdrahtet, ohne
/// Haken zum Anpassen. Genau das ist hier zugesagt: wer sich beschwert,
/// ausgesperrt worden zu sein, soll eine Kennung nennen können — und der Browser
/// soll keine zweite Fehlergestalt lernen müssen.
/// <c>Die_Abweisung_nennt_kein_Konto</c> nagelt beides fest. Gemeldet als
/// <c>bugs/abweisung-der-bremse-ist-kein-problemdokument.md</c>; kommt es, fällt
/// diese Kette weg.</para>
///
/// <para>Zwei kleinere Unterschiede stehen daneben, keiner davon trägt allein:
/// die Grenzen lägen in einer zweiten Datei statt neben den Routen (heute nagelt
/// <c>BremsenkarteTests</c> fest, dass jeder gebremste Pfad eine Route hat), und
/// Girders Optionsklasse bringt fremde Je-Pfad-Vorgaben mit (<c>/api/auth/login</c>
/// und sechs weitere aus Skillswap), die beim Binden nicht ersetzt, sondern
/// ergänzt werden — gemessen: <c>/api/auth/register</c> wurde bei 3 gebremst,
/// obwohl es diese Route hier nicht gibt.</para>
///
/// <para>Der <em>Zähler</em> von Girder war schon immer nachgemessen richtig, und
/// den benutzen wir: <c>IDistributedRateLimitStore</c>. Eigen ist hier nur die
/// Kette darüber.</para>
/// </remarks>
public static class Bremse
{
    /// <summary>Steht in der Antwort, wenn gebremst wurde.</summary>
    public const string KopfGrenze = "X-RateLimit-Limit";

    /// <summary>Wie viel im laufenden Fenster noch übrig ist.</summary>
    public const string KopfRest = "X-RateLimit-Remaining";

    /// <summary>Wann es wieder geht, in Sekunden.</summary>
    public const string KopfSpaeter = "Retry-After";

    /// <summary>
    /// Wenn keine Herkunft feststellbar ist. Alle namenlosen Aufrufer teilen
    /// sich dann einen Topf — enger, nie weiter: eine unbekannte Herkunft darf
    /// nicht der Weg an der Bremse vorbei sein.
    /// </summary>
    public const string Namenlos = "ohne-herkunft";

    /// <summary>Hängt die Bremse in die Kette.</summary>
    public static IApplicationBuilder UseBremse(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var einstellungen = app.ApplicationServices
            .GetRequiredService<Bremseinstellungen>();
        var speicher = app.ApplicationServices
            .GetRequiredService<IDistributedRateLimitStore>();

        return app.Use(async (context, weiter) =>
        {
            var pfad = context.Request.Path.Value ?? string.Empty;

            if (!einstellungen.Pfade.TryGetValue(pfad, out var grenze))
            {
                await weiter();
                return;
            }

            grenze *= Math.Max(1, einstellungen.Faktor);

            var ergebnis = await speicher.SlidingWindowIncrementAsync(
                Schluessel(pfad, Herkunft(context)),
                grenze,
                einstellungen.Fenster,
                context.RequestAborted);

            context.Response.Headers[KopfGrenze] =
                grenze.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers[KopfRest] =
                Math.Max(0, grenze - ergebnis.CurrentCount)
                    .ToString(CultureInfo.InvariantCulture);

            if (ergebnis.IsAllowed)
            {
                await weiter();
                return;
            }

            await Abweisen(context, einstellungen.Fenster);
        });
    }

    /// <summary>Der Zählerschlüssel: Pfad und Herkunft, sonst nichts.</summary>
    /// <remarks>
    /// Getrennt und öffentlich, damit ein Test ihn direkt lesen kann. Was hier
    /// <em>nicht</em> hineingeht, ist die eigentliche Zusage: kein Rumpf, keine
    /// Abfrage, keine E-Mail-Adresse.
    /// </remarks>
    public static string Schluessel(string pfad, string herkunft) =>
        $"bremse:{pfad}:{herkunft}";

    /// <summary>Wer fragt — die Adresse der Verbindung, nichts Mitgebrachtes.</summary>
    public static string Herkunft(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var adresse = context.Connection.RemoteIpAddress;

        if (adresse is null)
        {
            return Namenlos;
        }

        // IPv4 in IPv6-Kleid auf eine Schreibweise bringen: ::ffff:10.0.0.1 und
        // 10.0.0.1 sind derselbe Rechner und duerfen nicht zwei Toepfe bekommen.
        return adresse.IsIPv4MappedToIPv6
            ? adresse.MapToIPv4().ToString()
            : adresse.ToString();
    }

    private static async Task Abweisen(HttpContext context, TimeSpan fenster)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.Headers[KopfSpaeter] =
            ((int)fenster.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        context.Response.ContentType = "application/problem+json";

        // Dieselbe Form wie in den Diensten (RFC 9457), damit der Browser eine
        // Antwort bekommt und keine zweite Fehlergestalt lernen muss.
        //
        // `detail` sagt bewusst NICHTS ueber das Konto. "zu viele Versuche fuer
        // anna@..." waere die Aufzaehlung durch die Hintertuer — und die Bremse
        // weiss es gar nicht, weil sie den Rumpf nie liest.
        // Selbst serialisiert, nicht ueber WriteAsJsonAsync: das setzt den
        // Inhaltstyp auf `application/json` zurueck und macht aus dem
        // Problemdokument ein gewoehnliches. Gemessen — der Test darauf fiel.
        // ServiceDefaults.ProblemDetailsMiddleware macht es aus demselben Grund
        // genauso.
        await context.Response.WriteAsync(JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["type"] = "https://workertransfer.dev/problems/429",
                ["title"] = "Request failed",
                ["status"] = (int)HttpStatusCode.TooManyRequests,
                ["detail"] = "too many requests",
                ["correlationId"] = context.Request.Headers[Korrelation.Kopf].ToString()
            }));
    }
}
