using Noelia.Core.Identity;
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
/// Die Übersetzung von Ergebnissen in Statuscodes — an einer Job für alle
/// Routen.
/// </summary>
/// <remarks>
/// Getrennt geschrieben würde die erste Route, die 403 statt 404 antwortet,
/// verraten, dass es den Case gibt. Bei diesem Dienst wiegt das am
/// schwersten: schon die Existenz der Aussage „diese Person hört zu" kann
/// jemanden den Arbeitsplatz kosten.
/// </remarks>
public static class Responses
{
    /// <summary>Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.</summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// ResumeRequest ohne Rumpf sieht der Aufrufer trotzdem sein 503 und nur das
    /// Protokoll trägt eine unbehandelte Ausnahme; bei einer mit Rumpf reißt
    /// die Connection. Gemessen an <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Hängt die 503-Übersetzung an eine Gruppe.</summary>
    /// <remarks>
    /// Ein schweigender Ledger ist weder ein Ja noch ein Nein. Mit 404 zu
    /// antworten hieße zu behaupten, es gebe diesen Menschen nicht; den Status
    /// zu zeigen hieße, eine Release zu unterstellen, die niemand bestätigt
    /// hat. 503 sagt das eine Wahre: dieser Dienst kann gerade nicht antworten.
    /// <para>
    /// Als Filter je Gruppe statt als <c>catch</c> je Route — und an
    /// <em>allen</em> Gruppen, auch denen, die heute keinen Ledger anfassen.
    /// Die Route, die es vergisst, sieht aus wie eine funktionierende, bis der
    /// Ledger ausfällt.
    /// </para>
    /// </remarks>
    public static RouteGroupBuilder Abgesichert(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.AddEndpointFilter(async (call, weiter) =>
        {
            try
            {
                return await weiter(call);
            }
            catch (ConsentGateSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the consent ledger did not answer");

                return Geschrieben;
            }
        });

        return group;
    }

    /// <summary>Schreibt die Absage für einen unangemeldeten Aufrufer.</summary>
    public static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
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

        if (handelnder.Acting is not Capacity.ForCompany company)
        {
            // Eine Aussage über den Aufrufer, nicht über das Ziel: eine
            // Privatperson weiß, dass sie kein Unternehmen ist.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "reading a market status requires an active company");

            return null;
        }

        return company.Tenant;
    }

    /// <summary>Schreibt das Ergebnis einer ResumeRequest.</summary>
    public static async Task Write(
        HttpContext context,
        ResumeRequestResult result,
        CancellationToken cancellationToken,
        bool? aktiv,
        int erfolg = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (result)
        {
            case ResumeRequestResult.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(
                    Zu(erledigt.ResumeRequest, aktiv), cancellationToken);
                return;

            case ResumeRequestResult.NichtSichtbar:
                // Nicht vorhanden, nicht freigegeben, nicht meins — alles
                // dasselbe. Der Unterschied wäre hier besonders teuer.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such market status");
                return;

            case ResumeRequestResult.SchonGefragt:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "This company has already asked");
                return;

            case ResumeRequestResult.StateConflict konflikt:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict, "Request failed", konflikt.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {result}");
        }
    }

    /// <summary>Schreibt das Ergebnis eines Vorgangs.</summary>
    public static async Task Write(
        HttpContext context,
        CaseResult result,
        CancellationToken cancellationToken,
        int erfolg = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (result)
        {
            case CaseResult.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(Zu(erledigt.Case), cancellationToken);
                return;

            case CaseResult.Unbekannt:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such transfer");
                return;

            case CaseResult.NichtAnsprechbar:
                // Kein Status, keine Release, oder „gerade nicht" — alles
                // dasselbe nach außen. Sonst wäre der Endpunkt ein Orakel
                // darüber, wer zuhört.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such person");
                return;

            case CaseResult.LaeuftSchon:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "There is already a running transfer");
                return;

            case CaseResult.StateConflict konflikt:
                // Die Eingabe ist in Ordnung, der Zustand passt nicht.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict, "Request failed", konflikt.Grund);
                return;

            case CaseResult.Eingabe eingabe:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {result}");
        }
    }

    /// <summary>Ein MarketStatus auf der Leitung.</summary>
    public static MarketStatusV1 Zu(MarketStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new MarketStatusV1(
            status.Wer.Value,
            Verfuegbarkeiten.Wort(status.Verfuegbarkeit),
            status.Beschaeftigt,
            status.Notiz,
            // Abgeleitet mitgeschickt: sonst reimt sich jeder Client die Regel
            // selbst zusammen, und irgendeiner reimt sie falsch.
            status.Ansprechbar,
            status.GeaendertAm);
    }

    /// <summary>Eine ResumeRequest auf der Leitung.</summary>
    public static MarketRequestV1 Zu(MarketRequest request, bool? aktiv)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new MarketRequestV1(
            request.Id,
            request.Wer.Value,
            request.Firma.Value,
            RequestStates.Wort(request.State),
            request.AngelegtAm,
            request.BeantwortetAm,
            aktiv);
    }

    /// <summary>Ein Case auf der Leitung.</summary>
    public static TransferV1 Zu(Domain.Vorgaenge.Transfer caseItem)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        return new TransferV1(
            caseItem.Id,
            caseItem.Wer.Value,
            caseItem.Firma.Value,
            Transferstaende.Wort(caseItem.State),
            caseItem.BrauchtFreigabe,
            caseItem.FreigabeBestaetigt,
            caseItem.Nachricht,
            caseItem.Angebotstext,
            caseItem.Angebotsbeginn,
            caseItem.AngebotsgebuehrCent,
            caseItem.AngelegtAm,
            caseItem.GeaendertAm);
    }
}
