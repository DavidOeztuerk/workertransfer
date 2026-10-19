using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Application.Cases;
using WorkerTransfer.Assessment.Contracts;
using WorkerTransfer.Assessment.Domain.Cases;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Assessment.Api;

/// <summary><c>/assessments</c> — the task, the solution, the feedback.</summary>
/// <remarks>
/// <para><strong>Six routes, and the set is closed.</strong>
/// <c>ConditionsTests</c> writes them down by name; whoever adds a seventh
/// changes that test and answers, while doing so, whether it gives out a
/// statement about a human being (ADR-0042 §1).</para>
///
/// <para><strong>And one that expressly does not exist: declining.</strong>
/// No route, no state, no field. Whoever does not want a task does nothing; the
/// deadline passes, and that is indistinguishable from "did not manage it". A
/// polite decline button would be kinder to the company and would create
/// exactly the record ADR-0042 §3 forbids — "declined three times" is a fact
/// about a human being, it arises from nothing but individually reasonable
/// clicks, and afterwards it is there.</para>
///
/// <para><strong><c>member</c> suffices, no <c>admin</c>.</strong> Setting and
/// answering a work sample is the daily work in the company's name; it does not
/// bind the company. A second right in front of it would turn an invitation
/// into a spectator seat.</para>
/// </remarks>
public static class AssessmentEndpoints
{
    /// <summary>Maps the routes.</summary>
    public static IEndpointRouteBuilder MapAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // As one filter instead of a catch in every route, because the route
        // that forgets it looks like a working one until something fails.
        var assessments = app.MapGroup("/assessments").AddEndpointFilter(Intercept);

        MapCompanySide(assessments);
        MapPersonSide(assessments);
        MapShared(assessments);

        return app;
    }

    // -----------------------------------------------------------------------
    // The company's side
    // -----------------------------------------------------------------------

    private static void MapCompanySide(IEndpointRouteBuilder assessments)
    {
        assessments.MapPost("/", async (
            SetTaskV1? body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(principal) is not { } company)
            {
                await WithoutCompany(principal, context);
                return;
            }

            if (body is null || body.SubjectId == Guid.Empty)
            {
                await Unprocessable(context, "subject_id is required");
                return;
            }

            // Size and deadline are required, and the refusal says so instead of
            // making up a default: the person would take an invented number for
            // the company's statement (ADR-0042 §3).
            if (body.Hours is not { } hours)
            {
                await Unprocessable(context, "hours is required — the effort is stated up front");
                return;
            }

            if (body.DueAt is not { } deadline)
            {
                await Unprocessable(context, "due_at is required");
                return;
            }

            var caseItem = await mediator.Send(
                new SetTaskCommand(
                    new SubjectId(body.SubjectId),
                    company,
                    body.Title,
                    body.Task,
                    hours,
                    deadline),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;

            await context.Response.WriteAsJsonAsync(View(caseItem, context), cancellationToken);
        });

        assessments.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(principal) is not { } company)
            {
                await WithoutCompany(principal, context);
                return;
            }

            var ours = await mediator.Send(
                new CompanyCasesQuery(company), cancellationToken);

            // No total (ADR-0026): through the difference to the length it would
            // give away how many human beings have taken the view away from the
            // company.
            await context.Response.WriteAsJsonAsync(
                new CaseListV1([.. ours.Select(caseItem => View(caseItem, context))]),
                cancellationToken);
        });

        assessments.MapPost("/{id:guid}/evaluation", async (
            Guid id,
            EvaluateV1? body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(principal) is not { } company)
            {
                await WithoutCompany(principal, context);
                return;
            }

            if (Words.ReadOutcome(body?.Outcome) is not { } outcome)
            {
                await Unprocessable(context, "outcome is 'accepted' or 'rejected'");
                return;
            }

            // The text is required — on a rejection too. It is checked in the
            // aggregate, so that no handler and no second endpoint can get around
            // it; here it falls out as 422 (ADR-0042 §2).
            var caseItem = await mediator.Send(
                new EvaluateCommand(id, company, outcome, body?.Text), cancellationToken);

            await context.Response.WriteAsJsonAsync(View(caseItem, context), cancellationToken);
        });
    }

    // -----------------------------------------------------------------------
    // The person's side
    // -----------------------------------------------------------------------

    private static void MapPersonSide(IEndpointRouteBuilder assessments)
    {
        // `/me` before `/{id:guid}`: the constraint separates the two anyway,
        // but the order says what is meant.
        assessments.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            // NO ledger in front. Whoever has revoked would otherwise no longer
            // get at their own evaluations — and a judgement the judged does not
            // read is exactly what is not built here (ADR-0042 §2).
            var mine = await mediator.Send(
                new MyCasesQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new CaseListV1([.. mine.Select(caseItem => View(caseItem, context))]),
                cancellationToken);
        });

        assessments.MapPost("/{id:guid}/submission", async (
            Guid id,
            SubmitV1? body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            var caseItem = await mediator.Send(
                new SubmitCommand(id, actor.Subject, body?.Text, body?.Url),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(View(caseItem, context), cancellationToken);
        });
    }

    // -----------------------------------------------------------------------
    // What belongs to both
    // -----------------------------------------------------------------------

    private static void MapShared(IEndpointRouteBuilder assessments)
    {
        /*
         * ONE route for both sides, and that is no saving of lines.
         *
         * "The person sees the evaluation" (ADR-0042 §2) is no rule here but a
         * construction: there is no company view beside a person view, because
         * there is only one endpoint that gives out a case — and it builds its
         * document with the same method. Two views would be the place where the
         * two drift apart, and only months later, at the next field.
         *
         * A test compares the two responses byte for byte.
         */
        assessments.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            var caseItem = await mediator.Send(
                new CaseQuery(id, actor.Subject, Company(principal)), cancellationToken);

            await context.Response.WriteAsJsonAsync(View(caseItem, context), cancellationToken);
        });
    }

    // -----------------------------------------------------------------------

    /// <summary>The company from the token — never from the request.</summary>
    /// <remarks>
    /// The tenant comes from the verified token (ADR-0017/0018). Taking it from a
    /// header or a field would let the caller decide in whose name they speak.
    /// </remarks>
    private static TenantId? Company(ICurrentPrincipal principal) =>
        principal.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null;

    /// <summary>The one way from a case to what stands on the wire.</summary>
    /// <remarks>
    /// The clock comes from the container and not from <c>DateTimeOffset.UtcNow</c>,
    /// so that a test can let a deadline pass without waiting.
    /// </remarks>
    private static CaseV1 View(Case caseItem, HttpContext context)
    {
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();

        return new CaseV1(
            caseItem.Id,
            caseItem.Subject.Value,
            caseItem.Company.Value,
            caseItem.Assignment.Title,
            caseItem.Assignment.Text,
            caseItem.Assignment.Hours,
            caseItem.Assignment.Deadline,
            Words.Word(caseItem.StateAt(clock.GetUtcNow())),
            caseItem.SetAt,
            caseItem.ChangedAt)
        {
            Submission = caseItem.Submission is { } submission
                ? new SubmissionV1(submission.Text, submission.Url, submission.At)
                : null,
            Evaluation = caseItem.Evaluation is { } evaluation
                ? new EvaluationV1(
                    Words.Word(evaluation.Outcome), evaluation.Text, evaluation.At)
                : null
        };
    }

    private static Task WithoutCompany(ICurrentPrincipal principal, HttpContext context) =>
        principal.Current is null
            ? NotAuthenticated(context)
            // 403 is a statement about the caller and gives away nothing about
            // the human being they ask about.
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static Task Unprocessable(HttpContext context, string detail) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status422UnprocessableEntity, "Request failed", detail);

    /// <summary>
    /// What a filter returns that has written the response itself.
    /// </summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — a JSON <c>null</c> with headers, and those
    /// are already sent at that point. On a request with a body the connection
    /// then tears.
    /// </remarks>
    private static readonly IResult AlreadyWritten = Results.Empty;

    private static async ValueTask<object?> Intercept(
        EndpointFilterInvocationContext call, EndpointFilterDelegate next)
    {
        try
        {
            return await next(call);
        }
        catch (NoCase error)
        {
            // FOUR situations, ONE answer: does not exist; belongs to somebody
            // else; belongs to another company; nothing released (any more). A
            // code of its own for the last would be the "locked" hint that gives
            // away that there is something (ADR-0020 §1).
            await Write(call, StatusCodes.Status404NotFound, error.Message);

            return AlreadyWritten;
        }
        catch (InputError error)
        {
            await Write(call, StatusCodes.Status422UnprocessableEntity, error.Message);

            return AlreadyWritten;
        }
        catch (StepNotPossible error)
        {
            // 409 and not 422: the input was fine, the case simply stands
            // elsewhere.
            await Write(call, StatusCodes.Status409Conflict, error.Message);

            return AlreadyWritten;
        }
        catch (ConsentGateSilent error)
        {
            // Expressly no 404 and no empty list: both would be a statement about
            // a human being that stems from our outage (ADR-0020 §3).
            await Write(call, StatusCodes.Status503ServiceUnavailable, error.Message);

            return AlreadyWritten;
        }
    }

    private static Task Write(
        EndpointFilterInvocationContext call, int status, string detail) =>
        ProblemDetailsMiddleware.Write(
            call.HttpContext, status, "Request failed", detail);
}
