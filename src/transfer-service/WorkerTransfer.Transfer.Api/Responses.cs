using Noelia.Core.Identity;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Requests;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Application.Cases;
using WorkerTransfer.Transfer.Contracts;
using WorkerTransfer.Transfer.Domain.Requests;
using WorkerTransfer.Transfer.Domain.Market;
using WorkerTransfer.Transfer.Domain.Cases;

namespace WorkerTransfer.Transfer.Api;

/// <summary>
/// The translation of results into status codes — in one place for every
/// route.
/// </summary>
/// <remarks>
/// Written separately, the first route that answers 403 instead of 404 would
/// give away that the case exists. At this service that weighs heaviest: the
/// mere existence of the statement "this person is listening" can cost somebody
/// their job.
/// </remarks>
public static class Responses
{
    /// <summary>What a filter returns when it has written the response itself.</summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — a JSON <c>null</c> along with headers,
    /// and those have already gone out by then. On a request without a body the
    /// caller still sees its 503 and only the log carries an unhandled
    /// exception; on one with a body the connection tears. Measured at
    /// <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult AlreadyWritten = Results.Empty;

    /// <summary>Hangs the 503 translation on a group.</summary>
    /// <remarks>
    /// A silent ledger is neither a yes nor a no. Answering 404 would mean
    /// asserting this human being does not exist; showing the status would mean
    /// presuming a release nobody confirmed. 503 says the one true thing: this
    /// service cannot answer just now.
    /// <para>
    /// As a filter per group rather than a <c>catch</c> per route — and on
    /// <em>every</em> group, including those that touch no ledger today. The
    /// route that forgets it looks like a working one until the ledger goes
    /// out.
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

                return AlreadyWritten;
            }
        });

        return group;
    }

    /// <summary>Writes the refusal for a caller who is not signed in.</summary>
    public static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    /// <summary>The caller's company, or a refusal already written.</summary>
    public static async Task<TenantId?> Company(HttpContext context, ICurrentPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Current is not { } actor)
        {
            await NotAuthenticated(context);
            return null;
        }

        if (actor.Acting is not Capacity.ForCompany company)
        {
            // A statement about the caller, not about the target: a private
            // person knows that they are not a company.
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
                // Not present, not released, not mine — all the same thing.
                // The difference would be especially expensive here.
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
                // No status, no release, or "not right now" — all the same to
                // the outside. Otherwise the endpoint would be an oracle about
                // who is listening.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such person");
                return;

            case CaseResult.AlreadyRunning:
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
            status.Subject.Value,
            Availabilities.Word(status.Availability),
            status.IsEmployed,
            status.Notiz,
            // Abgeleitet mitgeschickt: sonst reimt sich jeder Client die Regel
            // selbst zusammen, und irgendeiner reimt sie falsch.
            status.Ansprechbar,
            status.ChangedAt);
    }

    /// <summary>Eine ResumeRequest auf der Leitung.</summary>
    public static MarketRequestV1 Zu(MarketRequest request, bool? aktiv)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new MarketRequestV1(
            request.Id,
            request.Subject.Value,
            request.Company.Value,
            RequestStates.Word(request.State),
            request.CreatedAt,
            request.BeantwortetAm,
            aktiv);
    }

    /// <summary>Ein Case auf der Leitung.</summary>
    public static TransferV1 Zu(Domain.Cases.Transfer caseItem)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        return new TransferV1(
            caseItem.Id,
            caseItem.Subject.Value,
            caseItem.Company.Value,
            TransferStates.Word(caseItem.State),
            caseItem.BrauchtFreigabe,
            caseItem.ReleaseConfirmed,
            caseItem.Nachricht,
            caseItem.OfferText,
            caseItem.OfferStart,
            caseItem.OfferFeeCents,
            caseItem.CreatedAt,
            caseItem.ChangedAt);
    }
}
