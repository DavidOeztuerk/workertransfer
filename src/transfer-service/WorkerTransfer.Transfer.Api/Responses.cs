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
    public static RouteGroupBuilder Guarded(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.AddEndpointFilter(async (call, next) =>
        {
            try
            {
                return await next(call);
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

    /// <summary>Writes the result of a market request.</summary>
    public static async Task Write(
        HttpContext context,
        MarketRequestResult result,
        CancellationToken cancellationToken,
        bool? active,
        int successStatus = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (result)
        {
            case MarketRequestResult.Done done:
                context.Response.StatusCode = successStatus;
                await context.Response.WriteAsJsonAsync(
                    ToWire(done.MarketRequest, active), cancellationToken);
                return;

            case MarketRequestResult.NotVisible:
                // Not present, not released, not mine — all the same thing.
                // The difference would be especially expensive here.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such market status");
                return;

            case MarketRequestResult.AlreadyAsked:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "This company has already asked");
                return;

            case MarketRequestResult.StateConflict conflict:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict, "Request failed", conflict.Reason);
                return;

            default:
                throw new InvalidOperationException($"Unhandled result: {result}");
        }
    }

    /// <summary>Writes the result of a case.</summary>
    public static async Task Write(
        HttpContext context,
        CaseResult result,
        CancellationToken cancellationToken,
        int successStatus = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(context);

        switch (result)
        {
            case CaseResult.Done done:
                context.Response.StatusCode = successStatus;
                await context.Response.WriteAsJsonAsync(ToWire(done.Case), cancellationToken);
                return;

            case CaseResult.Unknown:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such transfer");
                return;

            case CaseResult.NotApproachable:
                // No status, no release, or "not right now" — all the same to
                // the outside. Otherwise the endpoint would be an oracle about
                // who is listening.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such person");
                return;

            case CaseResult.AlreadyRunning:
                // web/src/features/work/api/transfers.ts (COMPANY_TRANSFER_CASES) matches this sentence.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "There is already a running transfer");
                return;

            case CaseResult.StateConflict conflict:
                // The input is fine, the state does not fit.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict, "Request failed", conflict.Reason);
                return;

            case CaseResult.InvalidInput invalid:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", invalid.Reason);
                return;

            default:
                throw new InvalidOperationException($"Unhandled result: {result}");
        }
    }

    /// <summary>A market status on the wire.</summary>
    public static MarketStatusV1 ToWire(MarketStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new MarketStatusV1(
            status.Subject.Value,
            Availabilities.Word(status.Availability),
            status.IsEmployed,
            status.Remark,
            // Derived and sent along: otherwise every client works the rule out
            // for itself, and one of them works it out wrongly.
            status.IsApproachable,
            status.ChangedAt);
    }

    /// <summary>A market request on the wire.</summary>
    public static MarketRequestV1 ToWire(MarketRequest request, bool? active)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new MarketRequestV1(
            request.Id,
            request.Subject.Value,
            request.Company.Value,
            RequestStates.Word(request.State),
            request.CreatedAt,
            request.AnsweredAt,
            active);
    }

    /// <summary>A case on the wire.</summary>
    public static TransferV1 ToWire(Domain.Cases.Transfer caseItem)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        return new TransferV1(
            caseItem.Id,
            caseItem.Subject.Value,
            caseItem.Company.Value,
            TransferStates.Word(caseItem.State),
            caseItem.NeedsRelease,
            caseItem.ReleaseConfirmed,
            caseItem.Message,
            caseItem.OfferText,
            caseItem.OfferStart,
            caseItem.OfferFeeCents,
            caseItem.CreatedAt,
            caseItem.ChangedAt);
    }
}
