using Noelia.Abstractions.Hosting;
using Microsoft.Extensions.Logging;
using Noelia.Infrastructure.Builder;
using Noelia.Infrastructure.Builder.Modules;
using Noelia.Infrastructure.Extensions;
using Noelia.Infrastructure.Security.Identity;
using Noelia.Infrastructure.Security.InputSanitization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Noelia.Dashboard;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.ServiceDefaults.Pruefungen;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// The one call every service makes, and the one that decides nothing a service
/// has to decide for itself.
/// </summary>
/// <remarks>
/// <para>What is here is here because eleven services answering it eleven ways
/// would be eleven chances to answer it wrong: how a token is verified, what a
/// failure looks like on the wire, in which order the pipeline runs. What is
/// <em>not</em> here is everything that is a decision — which database, which
/// repositories, whether there is an outbox. Those live in each service's own
/// <c>Add&lt;Dienst&gt;Infrastructure()</c>, where a reader can see them.</para>
///
/// <para><strong>Seit Girder 4 ist die Vorgabe eine Vorgabe.</strong> Vorher
/// nahm <c>AddSharedInfrastructure</c> ein Lambda, und dieses Lambda
/// <em>ersetzte</em> die Vorgabe, statt sie zu ergänzen. Wir listeten fünf
/// Module auf und bekamen fünf — und merkten nicht, was dadurch fehlte:
/// <strong>Serilog, Swagger, CORS, die JSON-Feineinstellungen und der
/// <c>HttpContextAccessor</c></strong>. Nichts davon war abgewählt; niemand
/// hatte sie je gewählt.</para>
///
/// <para>Der Beweis dafür, dass wir vom neuen Baumeister nichts benutzten, war
/// der Versionssprung selbst: 3.0.1 auf 4.0.1 kostete <em>null</em>
/// Quelltextänderungen. Wer nichts benutzt, merkt auch nichts.</para>
///
/// <para><c>UseDefaults()</c> ist ein Aufruf, kein Automatismus — wer ihn
/// weglässt, bekommt nichts, dieselbe Form wie EF ohne Anbieter. Und
/// <c>Without(modul, grund)</c> übersetzt ohne Begründung nicht, weshalb jede
/// Abweichung hier ihren Grund im Quelltext trägt statt in einem Kopf.</para>
/// </remarks>
public static class Dienstgrundlage
{
    /// <summary>
    /// Noelias Vorgabe, dazu der Handelnde — und kein Ausstellen.
    /// </summary>
    /// <remarks>
    /// <para><c>Principal</c> steht nicht in der Vorgabe, weil es eine
    /// Entscheidung verlangt, die Noelia nicht treffen darf. Wir treffen sie für
    /// alle elf gleich: jeder Dienst liest den Handelnden aus dem geprüften
    /// Token, denn darauf steht <c>Capacity</c> (ADR-0017).</para>
    ///
    /// <para><strong>Ausstellen kann nur identity-service</strong>
    /// (<see cref="AlsAussteller"/>). Das ist keine Konvention: ein zweiter
    /// Aussteller wäre eine zweite Stelle, die einen Handelnden erschaffen kann,
    /// und nichts weiter unten könnte die beiden auseinanderhalten.</para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <param name="environment">Where it runs.</param>
    /// <param name="dienstname">What it calls itself in logs and telemetry.</param>
    /// <param name="weitere">Modules only this service needs.</param>
    public static IServiceCollection AddWorkerTransferDefaults(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string dienstname,
        Action<NoeliaBuilder>? weitere = null)
    {
        // Die Eingabepruefung sieht NICHT in JSON-Ruempfe. Gemessen, nicht
        // vermutet.
        //
        // Sie laeuft vor allem anderen und kann kein Feld benennen — eine
        // Anfrage, die sie abweist, hat den Fachpruefer nie erreicht. Damit wird
        // aus einem 422, das sagt WELCHES Feld falsch ist, ein blankes 400
        // „malicious input". Gemessen an fuenf Faellen in drei Diensten:
        // `javascript:alert(1)` und `data:text/html,<script>` als Portfolio-Link,
        // dieselben zwei am Arbeitgeberprofil, und `../../etc/passwd` als
        // GitHub-Anmeldename. Alle fuenf werden weiterhin abgewiesen — nur sagt
        // die Antwort dem Menschen im Formular jetzt weniger als vorher.
        //
        // Das ist hier zu verschmerzen und anderswo nicht: unsere gesamte
        // Schreibflaeche ist gepruefte Fachschicht mit RFC-9457-Antworten samt
        // Feldnamen, wir bauen kein SQL aus Zeichenketten (EF mit Parametern),
        // und die Ausgabe entkommt React. Der Kantenfilter verdeckt hier also
        // bessere Pruefer, statt etwas zu decken.
        //
        // Query-String und die zwei Adresskoepfe bleiben geprueft — das ist die
        // Flaeche, auf der ein Wert ohne Fachpruefer ankommt.
        services.Configure<InputSanitizationOptions>(
            optionen => optionen.InspectJsonBodies = false);

        // Das Token kommt aus dem `Authorization`-Kopf ODER aus dem
        // `access`-Cookie, und beide Traeger werden gebraucht: Dienst-zu-Dienst
        // und CLI schicken den Kopf, der Browser sieht das httpOnly-Token nie und
        // kann es nur als Cookie zuruecksenden.
        //
        // Das stand elfmal, in jedem Dienst wortgleich. Es ist aber keine
        // Entscheidung eines Dienstes, sondern eine der Plattform — und elf
        // Stellen fuer dieselbe Aussage sind elf Gelegenheiten, sie beim
        // zwoelften Dienst zu vergessen. Dann kaeme jeder Schalter im Browser
        // mit 401 zurueck, obwohl die Anmeldung aussieht, als haette sie
        // funktioniert.
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            optionen => optionen.AuchAusDemCookie());

        // Eine Ablehnung durch eine Richtlinie schliesst die Antwort KURZ und
        // wirft nicht — `ProblemDetailsMiddleware` sieht sie nie. Ohne diese
        // Zeile faellt sie als nackter 401/403 mit leerem Rumpf heraus, ohne
        // Korrelationskennung, und die Oberflaeche bekaeme fuer dieselbe Sache
        // zwei Gestalten.
        //
        // Sie stand in identity-service, solange der der einzige Dienst mit
        // Richtlinien war. Seit die Firmenrechte in vier Diensten haengen,
        // gehoert sie hierher: eine Gestalt ueber alle Dienste.
        //
        // In einem Dienst OHNE Richtlinien kostet sie nichts: die
        // Autorisierungs-Zwischenschicht ruft diesen Handler nur fuer
        // Endpunkte, die Autorisierungsdaten tragen.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, Ablehnungsgestalt>();

        // DER NACHWEIS HAENGT HIER, weil das der eine Aufruf ist, den jeder
        // Dienst macht (ADR-0044).
        //
        // Der Grund steht schon in dieser Datei, eine Ueberschrift weiter oben:
        // "Wir listeten fuenf Module auf und bekamen fuenf — und merkten nicht,
        // was dadurch fehlte. Nichts davon war abgewaehlt; niemand hatte sie je
        // gewaehlt." Genau diese Krankheit haben die Zusagen dieses Baumes an
        // drei weiteren Stellen: sie sind aufgeschrieben und nicht ausfuehrbar.
        //
        // Und ein Test kann sie nicht heilen. `Adr0022Tests` und
        // `EntwurfsgrenzeTests` pruefen den BAUM; der KI-Zugang einer Person
        // steht in `KiZugangV1` — Anbieter, Adresse, Modell und Schluessel pro
        // Person zur Laufzeit —, und die Ziele der Egress-Grenze stehen in der
        // Umgebung. Wohin dieser Behaelter heute Abend spricht, weiss nur er
        // selbst.
        services.AddNachweis(configuration);

        return services.AddNoelia(configuration, environment, dienstname, girder =>
        {
            girder
                .UseDefaults()
                .Use(NoeliaModule.Principal)

                // WOHER DIE SCHLUESSEL KOMMEN, sagt Girder 4 nicht mehr selbst —
                // `NoeliaModule.Jwt` registriert den Dienst, nicht den
                // Schluesselbund. Das ist richtig: ob ein Dienst ausstellt oder
                // nur prueft, ist eine Aussage ueber seine Rolle im System.
                //
                // Heute teilen sich alle elf EIN Geheimnis (HS256), deshalb
                // ueberall dasselbe. Die Form, die wir wollen, steht schon
                // daneben: `Issue(privat, kid)` fuer identity, `VerifyOnly(
                // oeffentlich, kid)` fuer die anderen zehn. Das ist H3 und
                // braucht eine Schluesselverteilung, keine Codeaenderung hier.
                .UseJwt(jwt => jwt.FromSharedSecret())

                // DIE EGRESS-GRENZE, UND WOHER SIE IHRE HOSTS KENNT.
                //
                // `AddSovereignPlatform` buendelt vier Dinge: die Egress-Grenze,
                // die Maskierung im Protokoll, den Souveraenitaetsbericht und
                // eine Pruefspur.
                //
                // Die Pruefspur bleibt AN. Fuer eine fachliche Zeile genuegt
                // sie ADR-0012 nicht (dafuer `EfPruefspur`) — der
                // Dashboard-Zugriff ist aber keine: er hat keine Transaktion,
                // und ohne Senke verweigert das Dashboard mit 503.
                //
                // GEMESSEN, BEVOR DAS HIER STAND (`EgressTests`): der Waechter
                // WEIST AB statt zu protokollieren, und ein Containername faellt
                // NICHT unter „loopback und RFC1918" — `consent-service` ist
                // keine Adresse, die sich als IP lesen laesst. Naiv uebernommen
                // waere das der Ausfall jedes Dienst-zu-Dienst-Aufrufs gewesen,
                // und zwar erst im Stapel, nicht im Test.
                //
                // Die Hosts kommen deshalb aus der KONFIGURATION und nicht aus
                // einer Liste hier: `Consent__Adresse`, `Jobs__Adresse`,
                // `Auskunft__*`, `Erasure__Adressen__*` stehen ohnehin in der
                // Umgebung, und was ein Dienst ruft, hat er dort schon gesagt.
                // Eine zweite Liste waere die, die als Erste veraltet — und ihr
                // Veralten faellt niemandem auf, weil der Aufruf dann einfach
                // abgewiesen wird.
                //
                // Was NICHT in der Konfiguration steht, darf auch nicht
                // hinaus. Genau das ist der Zweck: ein Telemetriezug mit einer
                // eingebauten Vorgabeadresse, ein SDK, das nach Hause telefoniert.
                .AddSovereignPlatform(souveraen => Deklariere(
                    souveraen.Allow([.. GerufeneHosts(configuration)]),
                    configuration))

                // DIE BETRIEBSOBERFLAECHE, und sie ersetzt sieben eigene
                // Adressen (ADR-0045).
                //
                // `/noelia` traegt Uebersicht, Zusammensetzung, Konfiguration,
                // Sicherheitspruefungen, Souveraenitaet, KI, PFLICHTEN, Pruefspur,
                // Sitzungen, Bremsen und Gesundheit — samt `report.json` aus
                // DERSELBEN Lesung. `WorkerTransfer.Nachweis` hat das selbst
                // gebaut, bevor dieser Baum auf Noelia stand; es ist geloescht
                // und steht nicht daneben. Zwei Wege zu einer Aussage laufen
                // auseinander, und beim ersten Mal merkt es niemand.
                //
                // OHNE BERECHTIGUNG 404, nicht 403 — dieselbe Zusage wie vorher.
                // Eine Betriebsoberflaeche, deren Existenz man erraten kann, ist
                // selbst schon eine Auskunft. Das Geheimnis bleibt ein EIGENES
                // Papier: wer eine Mail anstossen darf, muss nicht erfahren,
                // welche KI-Anbieter diese Instanz benutzt.
                .UseDashboard(dashboard => dashboard
                    .At("/noelia")
                    .VisibleTo(Nachweisaufbau.Eingelassen)
                    .InspectConfiguration("Consent", "Draft", "Erasure")
                    .InProduction(
                        "Der Betriebsnachweis dieser Instanz — Empfaenger, "
                        + "Naht, Loeschkaskade — steht hinter einem eigenen "
                        + "Geheimnis und traegt keinen Wert und keinen Namen."))

                // Noelias Bremse steht in der Vorgabe, und sie ist in 4.0.2
                // nachgemessen in Ordnung (H2, Messung 1): sie bremst, sie zaehlt
                // je Herkunft, sie liest X-Forwarded-For nicht mehr, und ein
                // gefaelschtes "X-Forwarded-For: 127.0.0.1" hebt sie nicht auf.
                // Der Grund, sie hier trotzdem nicht zu fahren, hat mit Noelia
                // nichts zu tun und aendert sich auch nicht mehr:
                //
                // Ein Dienst hinter dem Gateway sieht als Herkunft die Adresse
                // DES GATEWAYS — fuer jeden Aufrufer dieselbe. Eine Bremse hier
                // wuerfe alle Menschen in einen Topf: wer als Erster fuenfmal
                // danebentippt, sperrt die ganze Welt aus. Die Bremse gehoert an
                // den Eingang, und dort steht seit 4.2.0 GIRDERS EIGENES MODUL,
                // konfiguriert in `ocelot.json` — der frueher hier genannte
                // Eigenbau `Bremse.cs` ist geloescht.
                .Without(
                    NoeliaModule.RateLimiting,
                    "ein Dienst hinter dem Gateway sieht als Herkunft nur das "
                    + "Gateway, also alle Aufrufer als einen — gebremst wird am "
                    + "Eingang, in ocelot.json")

                // Steht ohnehin nicht in der Vorgabe. Hier genannt, damit die
                // Entscheidung im Quelltext steht und nicht im Gedaechtnis.
                .Without(
                    NoeliaModule.HttpResponseCaching,
                    "ETag ist ein Zwischenspeicher beim Aufrufer, und fast alles "
                    + "hier steht hinter dem Einwilligungstor: ein 304 zeigte ein "
                    + "widerrufenes Profil weiter. Caching selbst bleibt AN — das "
                    + "Modul stellt Speicher nur bereit, und keine "
                    + "Einwilligungsabfrage ist ein ICacheableQuery")

                // Communication und Encryption stehen ohnehin nicht in der
                // Vorgabe — hier genannt, weil ein Grund, der nur in einem
                // Commit-Text steht, beim naechsten Leser nicht existiert.
                // Noelias eigener Kommentar sagt es: "Silence is not a
                // decision."
                //
                // Communication: gemessen in H2. Die Statuscodes sind heil (ein
                // 404 kommt als 404 an, EIN Aufruf am Ziel, keine drei). Es
                // verlangt aber `IEventBus`, und den liefert allein
                // Noelia.Messaging.MassTransit — also einen Broker, den wir
                // bewusst nicht betreiben. Dazu schickt `UseGateway = true` per
                // Vorgabe JEDEN Aufruf an den Dienst "gateway", und
                // `EnableResponseCaching = true` speichert jede GET-Antwort
                // fuenf Minuten. Und der Grund, der den Umstieg attraktiv
                // machte, ist erledigt: die Korrelationskette haengt seit 4.0.0
                // an jedem HttpClient der Fabrik.
                .Without(
                    NoeliaModule.Communication,
                    "verlangt IEventBus und damit einen Broker, den wir nicht "
                    + "betreiben; die Korrelationskette bekommen wir seit 4.0.0 "
                    + "ohne ihn")

                // Encryption: verlangt `IDataEncryptionService` UND
                // `IMasterKeyProvider`, beide aus Noelia.Redis oder einem
                // Geheimnisspeicher. Wir verschluesseln heute auf Feldebene
                // nichts. Wer damit anfaengt, entscheidet zuerst, WO der
                // Hauptschluessel liegt — und das ist dieselbe Frage wie bei
                // SecretManagement, nicht eine Zeile hier.
                .Without(
                    NoeliaModule.Encryption,
                    "verschluesselt wird heute auf Feldebene nichts; wer damit "
                    + "anfaengt, entscheidet zuerst, wo der Hauptschluessel liegt")

                // ResourceAuthorization bleibt draussen (seit 4.0.2 ein eigenes
                // Modul): Ressourcenrichtlinien ruft kein Endpunkt hier, und es
                // verlangt einen Speicher. `Authorization` selbst ist IN der
                // Vorgabe und bringt den PermissionPolicyProvider mit — genau
                // den, an dem die Firmenrechte in identity-service haengen.
                .Without(
                    NoeliaModule.ResourceAuthorization,
                    "ResourceRead und ResourceOwner ruft kein Endpunkt hier, und "
                    + "das Modul verlangt einen Speicher. Unsere Autorisierung "
                    + "sind die Permission-Richtlinien, und die entscheidet "
                    + "Mitgliedschaftsrecht aus der Tabelle statt aus Anspruechen")

                // Seit 4.1.0 ist die abriegelnde Zwischenschicht ein eigenes
                // Modul, getrennt vom Richtlinienanbieter — genau die Trennung,
                // die uns gefehlt hat. Vorher hing beides an `Authorization`, und
                // dann war die Wahl: entweder niemand beantwortet die
                // Permission-Richtlinien, oder alles ausserhalb der aufgezaehlten
                // Flaeche antwortet 401.
                //
                // `PermissionMiddleware` verlangt fuer JEDE Anfrage
                // Authentifizierung, ausser der Endpunkt traegt `[AllowAnonymous]`
                // oder eine eigene `IEndpointAccessPolicy` erklaert ihn fuer
                // oeffentlich. Fail-closed, und fuer ein System ohne oeffentliche
                // Flaeche richtig. Wir haben eine: `/auth/login`,
                // `/auth/register`, `/auth/session`, die Karriereseite unter
                // `/companies/by-slug/{k}`, die Gesundheitsproben — und die
                // Diensteingaenge hinter einem Geheimniskopf statt hinter einem
                // Token (`/notifications`, `/erasure`, `/internal/notify`).
                //
                // Gemessen, was sie kostet: mit ihr antwortet `POST
                // /notifications` mit 401 in Noelias Umschlag, obwohl der
                // Geheimniskopf stimmt — die Ablehnung faellt vor unserem
                // Fehlerdokument und vor dem Endpunkt.
                //
                // Sie einzuschalten hiesse, unsere oeffentliche Flaeche ein
                // ZWEITES Mal zu erklaeren, neben den Endpunkten und neben
                // `docs/routenkarte.yml`. Zwei Listen ueber dieselbe Frage gehen
                // auseinander, und die Karte ist die, die gefahren wird. Dazu
                // kommt: die Middleware liest Rechte aus ANSPRUECHEN, und unser
                // Token traegt keine — sie koennte also ohnehin nur durchwinken
                // oder ablehnen, nie erlauben.
                //
                // `Authorization` bleibt und ist deshalb hier NICHT genannt: der
                // PermissionPolicyProvider traegt die Firmenrechte.
                .Without(
                    NoeliaModule.PermissionEnforcement,
                    "unsere oeffentliche Flaeche steht an den Endpunkten und in "
                    + "docs/routenkarte.yml; sie hier ein zweites Mal zu erklaeren "
                    + "hiesse zwei Listen ueber dieselbe Frage. Der "
                    + "Richtlinienanbieter (Authorization) bleibt");

            weitere?.Invoke(girder);
        });
    }

    /// <summary>
    /// The extra a service needs to <em>issue</em> tokens. identity-service only.
    /// </summary>
    /// <remarks>
    /// Its own method with its own name so that a search for it finds exactly
    /// one composition root. A reviewer should be able to answer "who can mint a
    /// token here?" by grepping, not by reading eleven files.
    /// </remarks>
    /// <param name="girder">Der Baumeister dieses Dienstes.</param>
    public static NoeliaBuilder AlsAussteller(this NoeliaBuilder girder)
    {
        ArgumentNullException.ThrowIfNull(girder);

        return girder
            .Use(NoeliaModule.PasswordHashing)
            .Use(NoeliaModule.TokenSessions);
    }

    /// <summary>Die Kette, und wer ihre Reihenfolge bestimmt.</summary>
    /// <remarks>
    /// <para><strong>Die Reihenfolge gehört Noelia.</strong> Hier stand zuletzt
    /// Noelias Vorgabekette abgeschrieben, dreizehn Glieder lang, mit drei
    /// ausgelassenen Zeilen — nicht aus Gestaltung, sondern aus Zwang: die Kette
    /// wusste bis 4.0.2 von der Modulauswahl nichts und brach ohne die
    /// Auslassung beim Start ab. Seit 4.1.0 liest sie
    /// <c>NoeliaComposition</c>, also steht hier wieder der Aufruf ohne Lambda:
    /// dieselbe Kette, die jeder andere Noelia-Dienst fährt, und jede Abwahl
    /// oben wirkt einmal statt zweimal.</para>
    ///
    /// <para>Zwei Glieder kommen danach, und beide aus einem Grund:</para>
    ///
    /// <para><c>UseNoeliaPrincipal()</c>, weil <c>Principal</c> kein Teil der
    /// Vorgabekette ist — es hängt an einer Entscheidung, und die haben wir oben
    /// getroffen. Es steht hinter der Authentifizierung, weil der Handelnde aus
    /// dem <em>geprüften</em> Token gebaut wird.</para>
    ///
    /// <para><see cref="ProblemDetailsMiddleware"/> zuletzt, also am nächsten an
    /// den Endpunkten. Noelias eigene Fehlerbehandlung steht weiter außen und
    /// fängt damit nur, was diese hier durchreicht. Eine Gestalt über alle
    /// Dienste ist der Grund: ein Aufrufer soll nicht wissen müssen, wer
    /// geantwortet hat, um den Fehler zu lesen.</para>
    /// </remarks>
    /// <param name="app">The application being built.</param>
    /// <param name="environment">Where it runs.</param>
    /// <param name="dienstname">What it calls itself.</param>
    public static WebApplication UseWorkerTransferDefaults(
        this WebApplication app,
        IHostEnvironment environment,
        string dienstname)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Noelias Vorgabekette, ohne Lambda.
        //
        // Hier standen dreizehn abgeschriebene Zeilen mit drei Auslassungen. Sie
        // standen da, weil die Kette bis 4.0.2 jedes Glied bedingungslos rief und
        // von `Without(...)` nichts wusste: `UseRateLimiting()` haette jeden
        // Dienst beim Start abgebrochen. Die Kopie war also kein Entwurf, sondern
        // ein Zwang — und genau die Sorte Duplikat, die beim naechsten
        // Noelia-Release still falsch wird: kommt ein Glied hinzu, fehlt es hier,
        // kein Bau bricht, kein Test faellt, die Kette ist einfach kuerzer.
        //
        // Seit 4.1.0 liest die Kette `NoeliaComposition`. Jede Abwahl oben wirkt
        // damit auch hier — RateLimiting, HttpResponseCaching und
        // PermissionEnforcement fallen von selbst weg, und ihre Begruendung steht
        // an genau einer Stelle.
        app.UseNoelia(environment, dienstname);

        app.UseNoeliaPrincipal();
        app.UseMiddleware<ProblemDetailsMiddleware>();

        BerichteZusammensetzung(app, dienstname);

        return app;
    }

    /// <summary>Trägt jedes Ziel ins Souveränitätsverzeichnis.</summary>
    /// <remarks>
    /// <c>Allow</c> verhindert den undeklarierten Aufruf; erst
    /// <c>DeclareDependency</c> macht das Ziel im Bericht sichtbar. Ohne diese
    /// Zeile ist das Egress-Register leer und die Flottensicht zeigt nichts.
    /// Deklariert wird unter dem Konfigurationsschlüssel, damit im Bericht steht,
    /// <em>wofür</em> ein Host gerufen wird.
    /// </remarks>
    private static SovereignPlatformBuilder Deklariere(
        SovereignPlatformBuilder souveraen, IConfiguration configuration)
    {
        foreach (var (schluessel, adresse) in GerufeneZiele(configuration))
        {
            souveraen.DeclareDependency(schluessel, adresse);
        }

        return souveraen;
    }

    /// <summary>Konfigurationsschlüssel und Adresse jedes gerufenen Ziels.</summary>
    private static IEnumerable<(string Schluessel, string Adresse)> GerufeneZiele(
        IConfiguration configuration) =>
        configuration.AsEnumerable()
            .Where(eintrag => !Horchadresse(eintrag.Key)
                              && !string.IsNullOrWhiteSpace(eintrag.Value)
                              && Uri.TryCreate(eintrag.Value, UriKind.Absolute, out var ziel)
                              && (ziel.Scheme == Uri.UriSchemeHttp
                                  || ziel.Scheme == Uri.UriSchemeHttps))
            .Select(eintrag => (eintrag.Key, eintrag.Value!))
            .DistinctBy(eintrag => eintrag.Item2, StringComparer.OrdinalIgnoreCase)
            .OrderBy(eintrag => eintrag.Key, StringComparer.Ordinal);

    /// <summary>Wo dieser Dienst horcht — kein Ziel, das er ruft.</summary>
    private static bool Horchadresse(string schluessel) =>
        schluessel.Contains("URLS", StringComparison.OrdinalIgnoreCase)
        || schluessel.StartsWith("Kestrel", StringComparison.OrdinalIgnoreCase);

    /// <summary>Jeder Host, den dieser Dienst laut Konfiguration ruft.</summary>
    /// <remarks>
    /// <para><strong>Eine Ableitung, keine Liste.</strong> Gelesen wird die ganze
    /// Konfiguration; jeder Wert, der sich als absolute http- oder https-Adresse
    /// lesen lässt, gibt seinen Host her. Eine zweite Liste wäre die, die als
    /// Erste veraltet — und ihr Veralten fiele niemandem auf, weil ein nicht
    /// erfasster Aufruf von der Egress-Grenze einfach abgewiesen wird.</para>
    ///
    /// <para><strong>Sie stand zwischenzeitlich woanders, und der Grund ist
    /// weggefallen.</strong> ADR-0044 zog sie in ein eigenes <c>Zielkunde</c>,
    /// weil sie zwei Leser hatte: die Egress-Grenze und <c>wt.grenze.ziele</c>.
    /// Mit ADR-0045 berichtet Noelias Souveränitätsabschnitt die Ziele selbst,
    /// der zweite Leser ist fort — und eine Ableitung mit einem Leser gehört
    /// dorthin, wo sie gelesen wird.</para>
    ///
    /// <para><strong>Zwei Ziele stehen deshalb in der Umgebung, die früher nur
    /// im Quelltext standen</strong> — <c>Draft__Adresse</c> und
    /// <c>GitHub__Adresse</c>. Eine eingebaute Vorgabeadresse nach draußen ist
    /// genau das, was ein Souveränitätsbericht sichtbar machen soll; sie im
    /// Code zu lassen hieße, sie vor ihm zu verstecken.</para>
    /// </remarks>
    private static IReadOnlyList<string> GerufeneHosts(IConfiguration configuration) =>
    [
        .. configuration.AsEnumerable()
            .Select(eintrag => eintrag.Value)
            .Where(wert => !string.IsNullOrWhiteSpace(wert))
            .Select(wert =>
                Uri.TryCreate(wert, UriKind.Absolute, out var adresse)
                && (adresse.Scheme == Uri.UriSchemeHttp
                    || adresse.Scheme == Uri.UriSchemeHttps)
                    ? adresse.Host
                    : null)
            .Where(host => host is { Length: > 0 })
            .Select(host => host!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(host => host, StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>Sagt beim Start, was dieser Dienst fährt — und was er warum nicht.</summary>
    /// <remarks>
    /// <para><strong>Die Absicht steht in <c>Dienstgrundlage.cs</c>, die
    /// Wirklichkeit im Container.</strong> Bis 4.0.2 konnten die beiden
    /// auseinanderlaufen, ohne dass es jemand merkte: die Kette rief jedes Glied
    /// bedingungslos, ein <c>Without(...)</c> wirkte nur bei der Registrierung,
    /// und ob die Auslassung wirklich ankam, sah man erst am Absturz. Genau
    /// dafür gibt es <c>NoeliaComposition</c>, und ein Bericht, den niemand
    /// ausgibt, ist keiner.</para>
    ///
    /// <para><strong>Ausgegeben wird nur die Zahl der geführten Module und jede
    /// Auslassung mit ihrem Grund.</strong> Die vollständige Liste wäre bei
    /// neunzehn Einträgen zwölfmal dasselbe im Startprotokoll; die Auslassungen
    /// sind das, worüber jemand entschieden hat, und nur sie können von der
    /// Absicht abweichen. Wer alles sehen will, liest
    /// <c>NoeliaComposition.Included</c> aus dem Container.</para>
    ///
    /// <para>Kein Wert wandert dabei ins Protokoll: ein Modulname ist eine
    /// Aufzählung, ein Grund ein Satz, den wir selbst geschrieben haben.</para>
    /// </remarks>
    private static void BerichteZusammensetzung(WebApplication app, string dienstname)
    {
        var zusammensetzung = app.Services.GetService<NoeliaComposition>();

        if (zusammensetzung is null)
        {
            // Nicht werfen: der Bericht ist Auskunft, keine Zusage. Ein Dienst,
            // der wegen einer fehlenden Auskunft nicht startet, tauscht ein
            // kleines Problem gegen ein grosses.
            app.Logger.LogWarning(
                "Noelia meldet keine Zusammensetzung für {Dienst} — der Bericht bleibt leer",
                dienstname);

            return;
        }

        // MIT NAMEN, nicht nur mit einer Zahl. „19 Module in Betrieb" liest sich
        // wie eine Bestaetigung und ist keine: dass eine Entscheidung von oben
        // wirklich ankam, sieht man erst am Namen. Gemessen am 09.09.2026 stand
        // dieselbe 19 vor und nach einer neuen `AddSovereignPlatform`-Zeile —
        // die Zahl konnte den Unterschied nicht zeigen, den sie melden sollte.
        app.Logger.LogInformation(
            "Noelia für {Dienst}: {Gefuehrt} Module in Betrieb ({Module}), {Ausgelassen} ausgelassen",
            dienstname,
            zusammensetzung.Included.Count,
            string.Join(", ", zusammensetzung.Included),
            zusammensetzung.Excluded.Count);

        foreach (var (modul, grund) in zusammensetzung.Excluded)
        {
            app.Logger.LogInformation(
                "Noelia-Modul {Modul} nicht in Betrieb: {Grund}", modul, grund);
        }
    }
}
