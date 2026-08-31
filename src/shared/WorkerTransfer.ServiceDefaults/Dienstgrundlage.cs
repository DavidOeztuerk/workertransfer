using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    /// Girders Vorgabe, dazu der Handelnde — und kein Ausstellen.
    /// </summary>
    /// <remarks>
    /// <para><c>Principal</c> steht nicht in der Vorgabe, weil es eine
    /// Entscheidung verlangt, die Girder nicht treffen darf. Wir treffen sie für
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
        Action<GirderBuilder>? weitere = null) =>
        services.AddGirder(configuration, environment, dienstname, girder =>
        {
            girder
                .UseDefaults()
                .Use(GirderModule.Principal)

                // WOHER DIE SCHLUESSEL KOMMEN, sagt Girder 4 nicht mehr selbst —
                // `GirderModule.Jwt` registriert den Dienst, nicht den
                // Schluesselbund. Das ist richtig: ob ein Dienst ausstellt oder
                // nur prueft, ist eine Aussage ueber seine Rolle im System.
                //
                // Heute teilen sich alle elf EIN Geheimnis (HS256), deshalb
                // ueberall dasselbe. Die Form, die wir wollen, steht schon
                // daneben: `Issue(privat, kid)` fuer identity, `VerifyOnly(
                // oeffentlich, kid)` fuer die anderen zehn. Das ist H3 und
                // braucht eine Schluesselverteilung, keine Codeaenderung hier.
                .UseJwt(jwt => jwt.FromSharedSecret())

                // Girders Bremse steht in der Vorgabe, und sie ist in 4.0.2
                // nachgemessen in Ordnung (H2, Messung 1): sie bremst, sie zaehlt
                // je Herkunft, sie liest X-Forwarded-For nicht mehr, und ein
                // gefaelschtes "X-Forwarded-For: 127.0.0.1" hebt sie nicht auf.
                // Der Grund, sie hier trotzdem nicht zu fahren, hat mit Girder
                // nichts zu tun und aendert sich auch nicht mehr:
                //
                // Ein Dienst hinter dem Gateway sieht als Herkunft die Adresse
                // DES GATEWAYS — fuer jeden Aufrufer dieselbe. Eine Bremse hier
                // wuerfe alle Menschen in einen Topf: wer als Erster fuenfmal
                // danebentippt, sperrt die ganze Welt aus. Die Bremse gehoert an
                // den Eingang, und dort steht sie (src/gateway/.../Bremse.cs).
                .Without(
                    GirderModule.RateLimiting,
                    "ein Dienst hinter dem Gateway sieht als Herkunft nur das "
                    + "Gateway, also alle Aufrufer als einen — gebremst wird am "
                    + "Eingang, in Bremse.cs")

                // Steht ohnehin nicht in der Vorgabe. Hier genannt, damit die
                // Entscheidung im Quelltext steht und nicht im Gedaechtnis.
                .Without(
                    GirderModule.HttpResponseCaching,
                    "ETag ist ein Zwischenspeicher beim Aufrufer, und fast alles "
                    + "hier steht hinter dem Einwilligungstor: ein 304 zeigte ein "
                    + "widerrufenes Profil weiter. Caching selbst bleibt AN — das "
                    + "Modul stellt Speicher nur bereit, und keine "
                    + "Einwilligungsabfrage ist ein ICacheableQuery")

                // ResourceAuthorization bleibt draussen (seit 4.0.2 ein eigenes
                // Modul): Ressourcenrichtlinien ruft kein Endpunkt hier, und es
                // verlangt einen Speicher. `Authorization` selbst ist IN der
                // Vorgabe und bringt den PermissionPolicyProvider mit — genau
                // den, an dem die Firmenrechte in identity-service haengen.
                .Without(
                    GirderModule.ResourceAuthorization,
                    "ResourceRead und ResourceOwner ruft kein Endpunkt hier, und "
                    + "das Modul verlangt einen Speicher. Unsere Autorisierung "
                    + "sind die Permission-Richtlinien, und die entscheidet "
                    + "Mitgliedschaftsrecht aus der Tabelle statt aus Anspruechen");

            weitere?.Invoke(girder);
        });

    /// <summary>
    /// The extra a service needs to <em>issue</em> tokens. identity-service only.
    /// </summary>
    /// <remarks>
    /// Its own method with its own name so that a search for it finds exactly
    /// one composition root. A reviewer should be able to answer "who can mint a
    /// token here?" by grepping, not by reading eleven files.
    /// </remarks>
    /// <param name="girder">Der Baumeister dieses Dienstes.</param>
    public static GirderBuilder AlsAussteller(this GirderBuilder girder)
    {
        ArgumentNullException.ThrowIfNull(girder);

        return girder
            .Use(GirderModule.PasswordHashing)
            .Use(GirderModule.TokenSessions);
    }

    /// <summary>Die Kette, und wer ihre Reihenfolge bestimmt.</summary>
    /// <remarks>
    /// <para><strong>Die Reihenfolge gehört Girder.</strong> Vorher stand hier
    /// eine eigene Liste aus fünf Gliedern — und damit auch die Verantwortung
    /// dafür, dass zwei vertauschte Zeilen nichts kaputt machen. Jetzt steht
    /// hier der Aufruf ohne Lambda: dieselbe Kette, die jeder andere
    /// Girder-Dienst fährt.</para>
    ///
    /// <para>Zwei Glieder kommen danach, und beide aus einem Grund:</para>
    ///
    /// <para><c>UseGirderPrincipal()</c>, weil <c>Principal</c> kein Teil der
    /// Vorgabekette ist — es hängt an einer Entscheidung, und die haben wir oben
    /// getroffen. Es steht hinter der Authentifizierung, weil der Handelnde aus
    /// dem <em>geprüften</em> Token gebaut wird.</para>
    ///
    /// <para><see cref="ProblemDetailsMiddleware"/> zuletzt, also am nächsten an
    /// den Endpunkten. Girders eigene Fehlerbehandlung steht weiter außen und
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

        // Girders Vorgabekette, WORTGLEICH — bis auf eine ausgelassene Zeile.
        //
        // Eigentlich stuende hier `UseSharedInfrastructure(environment,
        // dienstname)` ohne Lambda, damit die Reihenfolge Girder gehoert. Das
        // geht nicht: die Vorgabekette ruft `UseRateLimiting()` fest, und die
        // Kette kennt die Modulauswahl nicht — `GirderComposition` kommt in
        // Girder.Infrastructure nicht vor. Die Dienstseite hat `Without(...)`,
        // die Kette hat nichts Entsprechendes.
        //
        // Ohne die Auslassung bricht jeder Dienst beim Start:
        // „UseRateLimiting() needs IDistributedRateLimitStore". Das ist kein
        // Fehler, sondern die RequiresProvider-Mechanik — nur eben auf einer
        // Seite, die von der Abwahl nichts weiss.
        //
        // Diese Liste ist deshalb eine KOPIE und keine Gestaltung. Sie faellt
        // weg, sobald die Kette die Auswahl liest.
        //
        // DREI Zeilen fehlen. Zwei gehoeren zu je einem `Without(...)` oben; das
        // Paar erzwingt nichts, wer dort abwaehlt und hier vergisst, bekommt
        // einen Startabbruch — besser als still, aber eben erst beim Starten.
        //
        // Die dritte ist `UsePermissions()`, und die ist keine Folge, sondern
        // eine EIGENE Entscheidung:
        //
        // `PermissionMiddleware` verlangt fuer JEDE Anfrage Authentifizierung,
        // ausser der Endpunkt traegt `[AllowAnonymous]` oder eine eigene
        // `IEndpointAccessPolicy` erklaert ihn fuer oeffentlich. Fail-closed,
        // und als Vorgabe fuer ein System, das keine oeffentliche Flaeche hat,
        // richtig. Wir haben eine: `/auth/login`, `/auth/register`,
        // `/auth/session`, die Karriereseite unter `/companies/by-slug/{k}`,
        // die Gesundheitsproben — und die Diensteingaenge hinter einem
        // Geheimniskopf statt hinter einem Token (`/notifications`,
        // `/erasure`, `/internal/notify`).
        //
        // Gemessen, was es kostet: mit der Zeile antwortet `POST /notifications`
        // mit 401 in Girders Umschlag, obwohl der Geheimniskopf stimmt — die
        // Ablehnung faellt vor unserem Fehlerdokument und vor dem Endpunkt.
        //
        // Sie einzuschalten hiesse, unsere oeffentliche Flaeche ein ZWEITES Mal
        // zu erklaeren, neben den Endpunkten und neben `docs/routenkarte.yml`.
        // Zwei Listen ueber dieselbe Frage gehen auseinander, und die Karte ist
        // die, die gefahren wird. Dazu kommt: die Middleware liest Rechte aus
        // ANSPRUECHEN, und unser Token traegt keine — sie koennte also ohnehin
        // nur durchwinken oder ablehnen, nie erlauben.
        app.UseSharedInfrastructure(environment, dienstname, kette => kette
            .UseSecurityHeaders()
            .UseCorrelationId()
            .UseRequestLogging()
            .UseTelemetry()
            .UseExceptionHandling()
            .UseInputSanitization()
            .UseSerilogLogging()
            .UseCors()
            .UseSwagger()
            // .UseRateLimiting()  — siehe Without(GirderModule.RateLimiting)
            .UseHealthCheckEndpoints()
            .UseAuth()
            .UseSecurityAudit());
            // .UsePermissions()   — siehe unten
            // .UseHttpCaching()   — siehe Without(GirderModule.HttpResponseCaching)

        app.UseGirderPrincipal();
        app.UseMiddleware<ProblemDetailsMiddleware>();

        return app;
    }
}
