using WorkerTransfer.Advisor.Api.Authorization;
using WorkerTransfer.ServiceDefaults.Roles;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Conversations;
using WorkerTransfer.Advisor.Application.Mandates;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Contracts;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Domain.Mandates;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Api;

/// <summary><c>/advisor</c> — the mandate, the conversations and their stages.</summary>
/// <remarks>
/// <para><strong>Two sides, separated in the address.</strong> Under
/// <c>/advisor/me/…</c> a human being acts for themselves; under
/// <c>/advisor/conversations</c> somebody acts for a company. A shared endpoint
/// would have to work out on every call who may currently do what — separated,
/// it stands in the path.</para>
///
/// <para><strong><c>member</c> is enough, no <c>admin</c>.</strong> Holding a
/// conversation is the daily work in the company's name. The one step that
/// really binds the company is the handover — and that lands at
/// transfer-service, where <c>POST /transfers/{id}/offer</c> has demanded an
/// <c>admin</c> since PBI-2. A second permission in front of it would turn an
/// invitation into a spectator seat.</para>
/// </remarks>
public static class AdvisorEndpoints
{
    /// <summary>Maps the advisor routes.</summary>
    public static IEndpointRouteBuilder MapAdvisorEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A silent ledger is neither a yes nor a no, and neither is a silent
        // lookup. 404 would say there is nothing; an empty list would claim the
        // same more quietly. 503 says the only true thing: this service cannot
        // answer right now (ADR-0020 §3).
        //
        // As one filter instead of a catch in every route, because the route
        // that forgets it looks like a working one until something fails.
        var advisor = app.MapGroup("/advisor").AddEndpointFilter(Intercept);

        MapMandate(advisor);
        MapMyConversations(advisor);
        MapCompanyConversations(advisor);

        return app;
    }

    // -----------------------------------------------------------------------
    // The mandate — one's own four values
    // -----------------------------------------------------------------------

    private static void MapMandate(IEndpointRouteBuilder advisor)
    {
        // Never `null` and never 404: "nothing said" IS a state. A `null` would
        // force the UI to invent a default — and the danger is that it invents
        // the wrong one.
        advisor.MapGet("/me/mandate", async (
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (actor.Current is not { } acting)
            {
                await NotSignedIn(context);
                return;
            }

            var mandate = await mediator.Send(
                new MyMandateQuery(acting.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(ToWire(mandate), cancellationToken);
        });

        advisor.MapPut("/me/mandate", async (
            WriteMandateV1? body,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (actor.Current is not { } acting)
            {
                await NotSignedIn(context);
                return;
            }

            try
            {
                // Whose mandate it is comes from the verified token and never
                // from the body: a subject id on the wire would be a way to
                // write somebody else's mandate.
                var mandate = await mediator.Send(
                    new WriteMandateCommand(
                        acting.Subject,
                        body?.EntryMonth,
                        body?.SalaryMin,
                        body?.SalaryMax,
                        body?.WorkloadPercent,
                        body?.ExcludedDomains),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(ToWire(mandate), cancellationToken);
            }
            catch (InputError error)
            {
                await Unprocessable(context, error.Message);
            }
        });
    }

    // -----------------------------------------------------------------------
    // The person's side
    // -----------------------------------------------------------------------

    private static void MapMyConversations(IEndpointRouteBuilder advisor)
    {
        advisor.MapGet("/me/conversations", async (
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (actor.Current is not { } acting)
            {
                await NotSignedIn(context);
                return;
            }

            var mine = await mediator.Send(
                new MyConversationsQuery(acting.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new MyConversationListV1([.. mine.Select(ForPerson)]), cancellationToken);
        });

        // Release a stage. The ledger gets the events; NO stage is stored here
        // (ADR-0037 decision 2).
        advisor.MapPost("/me/conversations/{id:guid}/advance", (
                Guid id,
                StageV1? body,
                IMediator mediator,
                ICurrentPrincipal actor,
                HttpContext context,
                CancellationToken cancellationToken) =>
            StageMove(
                id, body, mediator, actor, context, cancellationToken,
                (conversation, subject, stage) => new ReleaseStageCommand(conversation, subject, stage),
                // Without a value the smallest: a release must not be the
                // widest by accident.
                Stage.Profile));

        // And taking it back. Without a value EVERYTHING — whoever revokes
        // means the whole thing when in doubt, and a default that took only
        // the top stage would leave the rest standing without anybody noticing.
        advisor.MapPost("/me/conversations/{id:guid}/withdraw", (
                Guid id,
                StageV1? body,
                IMediator mediator,
                ICurrentPrincipal actor,
                HttpContext context,
                CancellationToken cancellationToken) =>
            StageMove(
                id, body, mediator, actor, context, cancellationToken,
                (conversation, subject, stage) => new RevokeStageCommand(conversation, subject, stage),
                Stage.Profile));

        advisor.MapPost("/me/conversations/{id:guid}/agree", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (actor.Current is not { } acting)
            {
                await NotSignedIn(context);
                return;
            }

            try
            {
                var view = await mediator.Send(
                    new AgreeCommand(id, acting.Subject), cancellationToken);

                await context.Response.WriteAsJsonAsync(ForPerson(view), cancellationToken);
            }
            catch (TransitionNotAllowed error)
            {
                await Conflict(context, error.Message);
            }
        });

        advisor.MapPost("/me/conversations/{id:guid}/end", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (actor.Current is not { } acting)
            {
                await NotSignedIn(context);
                return;
            }

            try
            {
                var view = await mediator.Send(
                    new EndCommand(id, acting.Subject, null), cancellationToken);

                await context.Response.WriteAsJsonAsync(ForPerson(view), cancellationToken);
            }
            catch (TransitionNotAllowed error)
            {
                await Conflict(context, error.Message);
            }
        });
    }

    // -----------------------------------------------------------------------
    // The company's side
    // -----------------------------------------------------------------------

    private static void MapCompanyConversations(IEndpointRouteBuilder advisor)
    {
        advisor.MapPost("/conversations", async (
            OpenConversationV1? body,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            if (body is null || body.SubjectId == Guid.Empty)
            {
                await Unprocessable(context, "subject_id is required");
                return;
            }

            try
            {
                var (view, created) = await mediator.Send(
                    new OpenConversationCommand(
                        new SubjectId(body.SubjectId), company, body.Note),
                    cancellationToken);

                context.Response.StatusCode =
                    created ? StatusCodes.Status201Created : StatusCodes.Status200OK;

                await context.Response.WriteAsJsonAsync(ForCompany(view), cancellationToken);
            }
            catch (InputError error)
            {
                await Unprocessable(context, error.Message);
            }
        })
        // A first contact to a person who did not come forward: at least a
        // `recruiter` (ADR-0048), resolved from identity-service's membership
        // table per request. The check stands BEFORE the body and the person
        // are looked at.
        .RequireAuthorization(AdminPermissionExtensions.Policy(AdvisorPermissions.Open));

        advisor.MapGet("/conversations", async (
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            var theirs = await mediator.Send(
                new CompanyConversationsQuery(company), cancellationToken);

            // No total (ADR-0026): through the difference to the length it
            // would reveal how many conversations are NOT at stage 1 right now.
            await context.Response.WriteAsJsonAsync(
                new ConversationListV1([.. theirs.Select(ForCompany)]), cancellationToken);
        });

        advisor.MapGet("/conversations/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            var view = await mediator.Send(
                new CompanyConversationQuery(id, company), cancellationToken);

            await context.Response.WriteAsJsonAsync(ForCompany(view), cancellationToken);
        });

        // The agreement becomes a case. The triangle consensus lives in
        // transfer-service and is not rebuilt here — this route calls its door
        // and passes the answer on honestly.
        advisor.MapPost("/conversations/{id:guid}/hand-over", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            try
            {
                var caseItem = await mediator.Send(
                    new HandOverCommand(id, company), cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, object?> { ["transfer_id"] = caseItem },
                    cancellationToken);
            }
            catch (TransitionNotAllowed error)
            {
                await Conflict(context, error.Message);
            }
            catch (HandoverRefused error)
            {
                // A refusal from there is a statement about the conditions of
                // this case — not about this service, and not a 500.
                await Conflict(context, error.Message);
            }
        });

        advisor.MapPost("/conversations/{id:guid}/end", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            try
            {
                var view = await mediator.Send(
                    new EndCommand(id, null, company), cancellationToken);

                // Without the mandate and without the civil name: an ended
                // conversation is no reason to fetch anything about a human
                // being once more.
                await context.Response.WriteAsJsonAsync(ForCompany(view), cancellationToken);
            }
            catch (TransitionNotAllowed error)
            {
                await Conflict(context, error.Message);
            }
        });
    }

    // -----------------------------------------------------------------------

    private static async Task StageMove(
        Guid id,
        StageV1? body,
        IMediator mediator,
        ICurrentPrincipal actor,
        HttpContext context,
        CancellationToken cancellationToken,
        Func<Guid, SubjectId, Stage, IRequest<Stage>> build,
        Stage defaultStage)
    {
        if (actor.Current is not { } acting)
        {
            await NotSignedIn(context);
            return;
        }

        if (body?.Stage is { } named && StageCapabilities.FromWire(named) is null)
        {
            await Unprocessable(context, "a stage is 1, 2 or 3");
            return;
        }

        // `defaultStage` and not the keyword `default`: that would be
        // Stage.None, and an advance without a value would then release
        // nothing at all while answering 200.
        var stage = StageCapabilities.FromWire(body?.Stage) ?? defaultStage;

        var stands = await mediator.Send(build(id, acting.Subject, stage), cancellationToken);

        await context.Response.WriteAsJsonAsync(
            new StageV1(StageCapabilities.ToWire(stands)), cancellationToken);
    }

    /// <summary>The company from the token — never from the request.</summary>
    /// <remarks>
    /// The tenant comes from the verified token (ADR-0017/0018). Taking it from
    /// a header or a field would let the caller decide in whose name they
    /// speak.
    /// </remarks>
    private static TenantId? Company(ICurrentPrincipal actor) =>
        actor.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null;

    private static Task WithoutCompany(ICurrentPrincipal actor, HttpContext context) =>
        actor.Current is null
            ? NotSignedIn(context)
            // 403 is a statement about the caller and reveals nothing about the
            // human being they are asking about.
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    private static Task NotSignedIn(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static Task Unprocessable(HttpContext context, string detail) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status422UnprocessableEntity, "Request failed", detail);

    private static Task Conflict(HttpContext context, string detail) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status409Conflict, "Request failed", detail);

    /// <summary>
    /// What a filter returns that has written the response itself.
    /// </summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — a JSON <c>null</c> plus headers, and those
    /// have already been sent at that point. On a request with a body the
    /// connection then tears.
    /// </remarks>
    private static readonly IResult Written = Results.Empty;

    private static async ValueTask<object?> Intercept(
        EndpointFilterInvocationContext call, EndpointFilterDelegate next)
    {
        try
        {
            return await next(call);
        }
        catch (NoConversation error)
        {
            // THREE situations, ONE answer: does not exist, no longer stands at
            // any stage, or the company is excluded. A code of its own for the
            // second or third would be the "locked" hint that reveals there is
            // something.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status404NotFound,
                "Request failed", error.Message);

            return Written;
        }
        catch (NotYours error)
        {
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status404NotFound,
                "Request failed", error.Message);

            return Written;
        }
        catch (ConsentRefused error)
        {
            // The ledger answered, and it said no. That is a statement about the
            // CALLER — they wanted to release for a different human being — and
            // reveals nothing about that human being.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status403Forbidden,
                "Request failed", error.Message);

            return Written;
        }
        catch (ConsentGateSilent error)
        {
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", error.Message);

            return Written;
        }
        catch (LookupSilent error)
        {
            // Expressly no empty result and no 404: both would be a statement
            // about a human being that stems from our outage.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", error.Message);

            return Written;
        }
    }

    private static MandateV1 ToWire(Mandate mandate) =>
        new(
            mandate.EntryMonth,
            mandate.SalaryMin,
            mandate.SalaryMax,
            mandate.WorkloadPercent,
            mandate.ExcludedDomains,
            mandate.UpdatedAt);

    private static MyConversationV1 ForPerson(ConversationView view) =>
        new(
            view.Conversation.Id,
            view.Conversation.Company.Value,
            ConversationStates.Word(view.Conversation.State),
            StageCapabilities.ToWire(view.Stage),
            view.Conversation.Occasion,
            view.Conversation.OpenedAt,
            view.Conversation.UpdatedAt);

    /// <summary>
    /// What the company sees — and what does not even appear as a field.
    /// </summary>
    /// <remarks>
    /// The selection lives in <see cref="ConversationView.Build"/> and not here:
    /// the same decision in two places would be one the second place forgets
    /// some day. This method only copies — what is <c>null</c> is absent on the
    /// wire.
    /// </remarks>
    private static ConversationV1 ForCompany(ConversationView view) =>
        new(
            view.Conversation.Id,
            view.Conversation.Subject.Value,
            ConversationStates.Word(view.Conversation.State),
            StageCapabilities.ToWire(view.Stage),
            view.Conversation.Occasion,
            view.Conversation.OpenedAt,
            view.Conversation.UpdatedAt)
        {
            EntryMonth = view.EntryMonth,
            WorkloadPercent = view.WorkloadPercent,
            SalaryMin = view.SalaryMin,
            SalaryMax = view.SalaryMax,
            Name = view.Name,
            Email = view.Email
        };
}
