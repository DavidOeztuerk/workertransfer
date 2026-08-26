using Girder.Core.Identity;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Anfragen;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Application.Vorgaenge;
using WorkerTransfer.Transfer.Contracts;
using WorkerTransfer.Transfer.Domain.Anfragen;
using WorkerTransfer.Transfer.Domain.Markt;
using WorkerTransfer.Transfer.Domain.Vorgaenge;

namespace WorkerTransfer.Transfer.Api;

/// <summary>
/// Die Übersetzung von Ergebnissen in Statuscodes — an einer Stelle für alle
/// Routen.
/// </summary>
/// <remarks>
/// Getrennt geschrieben würde die erste Route, die 403 statt 404 antwortet,
/// verraten, dass es den Vorgang gibt. Bei diesem Dienst wiegt das am
/// schwersten: schon die Existenz der Aussage „diese Person hört zu" kann
/// jemanden den Arbeitsplatz kosten.
/// </remarks>
public static class Antworten
{
    /// <summary>Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.</summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// Anfrage ohne Rumpf sieht der Aufrufer trotzdem sein 503 und nur das
    /// Protokoll trägt eine unbehandelte Ausnahme; bei einer mit Rumpf reißt
    /// die Verbindung. Gemessen an <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Hängt die 503-Übersetzung an eine Gruppe.</summary>
    /// <remarks>
    /// Ein schweigender Ledger ist weder ein Ja noch ein Nein. Mit 404 zu
    /// antworten hieße zu behaupten, es gebe diesen Menschen nicht; den Status
    /// zu zeigen hieße, eine Freigabe zu unterstellen, die niemand bestätigt
    /// hat. 503 sagt das eine Wahre: dieser Dienst kann gerade nicht antworten.
    /// <para>
    /// Als Filter je Gruppe statt als <c>catch</c> je Route — und an
    /// <em>allen</em> Gruppen, auch denen, die heute keinen Ledger anfassen.
    /// Die Route, die es vergisst, sieht aus wie eine funktionierende, bis der
    /// Ledger ausfällt.
    /// </para>
    /// </remarks>
    public static RouteGroupBuilder Abgesichert(RouteGroupBuilder gruppe)
    {
        ArgumentNullException.ThrowIfNull(gruppe);

        gruppe.AddEndpointFilter(async (aufruf, weiter) =>
        {
            try
            {
                return await weiter(aufruf);
            }
            catch (EinwilligungSchweigt)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the consent ledger did not answer");

                return Geschrieben;
            }
        });

        return gruppe;
    }

    /// <summary>Schreibt die Absage für einen unangemeldeten Aufrufer.</summary>
    public static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    /// <summary>Die Firma des Aufrufers, oder eine schon geschriebene Absage.</summary>
    public static async Task<TenantId?> Firma(HttpContext context, ICurrentPrincipal akteur)
    {
        ArgumentNullException.ThrowIfNull(akteur);

        if (akteur.Current is not { } handelnder)
        {
            await NichtAngemeldet(context);
            return null;
        }

        if (handelnder.Acting is not Capacity.ForCompany firma)
        {
            // Eine Aussage über den Aufrufer, nicht über das Ziel: eine
            // Privatperson weiß, dass sie kein Unternehmen ist.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "reading a market status requires an active company");

            return null;
        }

        return firma.Tenant;
    }

    /// <summary>Schreibt das Ergebnis einer Anfrage.</summary>
    public static async Task Schreibe(
        HttpContext context,
        Anfrageergebnis ergebnis,
        CancellationToken cancellationToken,
        bool? aktiv,
        int erfolg = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (ergebnis)
        {
            case Anfrageergebnis.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(
                    Zu(erledigt.Anfrage, aktiv), cancellationToken);
                return;

            case Anfrageergebnis.NichtSichtbar:
                // Nicht vorhanden, nicht freigegeben, nicht meins — alles
                // dasselbe. Der Unterschied wäre hier besonders teuer.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such market status");
                return;

            case Anfrageergebnis.SchonGefragt:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "This company has already asked");
                return;

            case Anfrageergebnis.Zustandskonflikt konflikt:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status409Conflict, "Request failed", konflikt.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {ergebnis}");
        }
    }

    /// <summary>Schreibt das Ergebnis eines Vorgangs.</summary>
    public static async Task Schreibe(
        HttpContext context,
        Vorgangsergebnis ergebnis,
        CancellationToken cancellationToken,
        int erfolg = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (ergebnis)
        {
            case Vorgangsergebnis.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(Zu(erledigt.Vorgang), cancellationToken);
                return;

            case Vorgangsergebnis.Unbekannt:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such transfer");
                return;

            case Vorgangsergebnis.NichtAnsprechbar:
                // Kein Status, keine Freigabe, oder „gerade nicht" — alles
                // dasselbe nach außen. Sonst wäre der Endpunkt ein Orakel
                // darüber, wer zuhört.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such person");
                return;

            case Vorgangsergebnis.LaeuftSchon:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "There is already a running transfer");
                return;

            case Vorgangsergebnis.Zustandskonflikt konflikt:
                // Die Eingabe ist in Ordnung, der Zustand passt nicht.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status409Conflict, "Request failed", konflikt.Grund);
                return;

            case Vorgangsergebnis.Eingabe eingabe:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {ergebnis}");
        }
    }

    /// <summary>Ein Marktstatus auf der Leitung.</summary>
    public static MarktstatusV1 Zu(Marktstatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new MarktstatusV1(
            status.Wer.Value,
            Verfuegbarkeiten.Wort(status.Verfuegbarkeit),
            status.Beschaeftigt,
            status.Notiz,
            // Abgeleitet mitgeschickt: sonst reimt sich jeder Client die Regel
            // selbst zusammen, und irgendeiner reimt sie falsch.
            status.Ansprechbar,
            status.GeaendertAm);
    }

    /// <summary>Eine Anfrage auf der Leitung.</summary>
    public static MarktanfrageV1 Zu(Marktanfrage anfrage, bool? aktiv)
    {
        ArgumentNullException.ThrowIfNull(anfrage);

        return new MarktanfrageV1(
            anfrage.Id,
            anfrage.Wer.Value,
            anfrage.Firma.Value,
            Anfragestaende.Wort(anfrage.Stand),
            anfrage.AngelegtAm,
            anfrage.BeantwortetAm,
            aktiv);
    }

    /// <summary>Ein Vorgang auf der Leitung.</summary>
    public static TransferV1 Zu(Domain.Vorgaenge.Transfer vorgang)
    {
        ArgumentNullException.ThrowIfNull(vorgang);

        return new TransferV1(
            vorgang.Id,
            vorgang.Wer.Value,
            vorgang.Firma.Value,
            Transferstaende.Wort(vorgang.Stand),
            vorgang.BrauchtFreigabe,
            vorgang.FreigabeBestaetigt,
            vorgang.Nachricht,
            vorgang.Angebotstext,
            vorgang.Angebotsbeginn,
            vorgang.AngebotsgebuehrCent,
            vorgang.AngelegtAm,
            vorgang.GeaendertAm);
    }
}
