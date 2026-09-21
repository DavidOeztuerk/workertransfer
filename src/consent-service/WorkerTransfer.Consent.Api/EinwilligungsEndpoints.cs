using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Consent.Application.Einwilligung;
using WorkerTransfer.Consent.Domain.Ledger;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Consent.Api;

/// <summary>What a caller sends to give a permission.</summary>
/// <remarks>
/// <para><strong>The wire names stand there expressly.</strong> This platform's
/// wire is snake_case; without <c>[JsonPropertyName]</c> <c>SubjectId</c> would
/// fall back to <c>subjectId</c> while the interface sends <c>subject_id</c>
/// (<c>apps/web/src/consent/client.ts:38,63</c>).</para>
///
/// <para>What that cost was, measured, no silent damage but a complete outage:
/// the value arrived as <c>Guid.Empty</c>, the guard compared it with the caller
/// and answered <c>403 „a consent belongs to its subject"</c>. <strong>Through
/// the interface nobody could release anything or take it back</strong> — at the
/// service every other one leans on. That nothing wrong got into the ledger is
/// the guard's doing, not this line's.</para>
/// </remarks>
/// <param name="Reason">Optional: giving a permission needs no justification.</param>
public sealed record GrantBody(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("reason")] string? Reason = null);

/// <summary>What a caller sends to take one back.</summary>
/// <param name="Reason">Mandatory: withdrawing must always be explainable.</param>
public sealed record RevokeBody(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>What a caller sends to ask about one pair.</summary>
public sealed record CheckBody(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability);

/// <summary>What a caller sends to ask about many pairs at once.</summary>
public sealed record CheckBatchBody(IReadOnlyList<CheckBody> Pairs);

/// <summary><c>/consent/*</c> — the ledger's whole surface.</summary>
public static class ConsentEndpoints
{
    /// <summary>Maps the five endpoints of the ledger.</summary>
    public static IEndpointRouteBuilder MapConsentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var consent = app.MapGroup("/consent");

        consent.MapPost("/grant", async (
            GrantBody body,
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

            var result = await mediator.Send(
                new GrantConsentCommand(
                    actor.Subject, Company(actor),
                    body.SubjectId, body.Capability, body.Reason),
                cancellationToken);

            await Respond(context, result, cancellationToken);
        });

        consent.MapPost("/revoke", async (
            RevokeBody body,
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

            var result = await mediator.Send(
                new RevokeConsentCommand(
                    actor.Subject, Company(actor),
                    body.SubjectId, body.Capability, body.Reason),
                cancellationToken);

            await Respond(context, result, cancellationToken);
        });

        // Deliberately takes no subject — not in the path and not as a query.
        // A foreign list would say which OTHER companies hold access to
        // somebody, which is exactly what this page exists to make visible to
        // its owner and to nobody else.
        consent.MapGet("/me", async (
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

            var releases = await mediator.Send(
                new MyReleasesQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                releases.Select(release => new Dictionary<string, string>
                {
                    ["capability"] = release.Capability.Value,
                    ["granted_at"] = release.Since.ToString("O")
                }),
                cancellationToken);
        });

        // WITH the reason, unlike every cross-subject read. The withdrawal
        // reason is free text the person wrote about themselves; towards them
        // there is no ground to withhold it. Outwards it stays hidden — that is
        // the difference between "belongs to them" and "is anybody else's
        // business".
        consent.MapGet("/me/history", async (
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

            var history = await mediator.Send(
                new MyHistoryQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                history.Select(entry => new Dictionary<string, string?>
                {
                    ["capability"] = entry.Capability.Value,
                    ["action"] = entry.Action.ToString().ToUpperInvariant(),
                    ["recorded_at"] = entry.RecordedAt.ToString("O"),
                    ["reason"] = entry.Reason?.Value
                }),
                cancellationToken);
        });

        // Open to any authenticated caller about any person — that is what makes
        // the ledger usable as an enabler. It answers WITHOUT the withdrawal
        // reason, because that reason is free text somebody wrote about
        // themselves and must not ride along on a query anyone may issue.
        consent.MapPost("/check", async (
            CheckBody body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is null)
            {
                await NotAuthenticated(context);
                return;
            }

            var result = await mediator.Send(
                new CheckConsentQuery(body.SubjectId, body.Capability),
                cancellationToken);

            switch (result)
            {
                case AuditResult.State state:
                    await context.Response.WriteAsJsonAsync(
                        Response(body.SubjectId, body.Capability, state.Value),
                        cancellationToken);
                    return;

                case AuditResult.Malformed malformed:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed", malformed.Detail);
                    return;
            }
        });

        // One question in one round trip, not a stock answer: the read stays
        // synchronous and nothing is cached (ADR-0013). It exists because a page
        // of candidates cost up to forty separate calls — 1.7 to 8.8 seconds,
        // longer than an interface waits.
        consent.MapPost("/check-batch", async (
            CheckBatchBody body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is null)
            {
                await NotAuthenticated(context);
                return;
            }

            if (body.Pairs is not { Count: > 0 })
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "at least one pair is required");
                return;
            }

            // Derived, not chosen: one page is fifty people and a person can be
            // asked about two capabilities. A batch makes asking cheaper, and a
            // ceiling keeps that difference small.
            if (body.Pairs.Count > ConsentLimits.HoechsteSammelgroesse)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity, "Request failed",
                    $"at most {ConsentLimits.HoechsteSammelgroesse} pairs per request");
                return;
            }

            var result = await mediator.Send(
                new BatchCheckQuery(
                    [.. body.Pairs.Select(pair =>
                        new CheckConsentQuery(pair.SubjectId, pair.Capability))]),
                cancellationToken);

            switch (result)
            {
                case BatchCheckResult.States states:
                    // In the order of the questions, one per pair. The order is
                    // part of the contract: the caller maps answers onto its
                    // rows, and a pair asked twice would otherwise be ambiguous.
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, object>
                        {
                            ["results"] = states.Values
                                .Select((state, i) => Response(
                                    body.Pairs[i].SubjectId, body.Pairs[i].Capability, state))
                                .ToArray()
                        },
                        cancellationToken);
                    return;

                case BatchCheckResult.Malformed malformed:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed", malformed.Detail);
                    return;
            }
        });

        return app;
    }

    /// <summary>
    /// The cross-subject answer: granted, deleted, and no reason.
    /// </summary>
    /// <remarks>
    /// Built here from the domain state rather than handed out as it is. The
    /// state carries the withdrawal reason because the subject's own views need
    /// it; this shape has no field for one, so it cannot leak by accident.
    /// </remarks>
    private static Dictionary<string, object> Response(
        Guid subjectId, string skill, ConsentState state) => new()
    {
        ["subject_id"] = subjectId,
        ["capability"] = skill,
        ["granted"] = state.Granted,
        ["deleted"] = state.Deleted
    };

    private static async Task Respond(
        HttpContext context,
        ConsentResult result,
        CancellationToken cancellationToken)
    {
        switch (result)
        {
            case ConsentResult.State state:
                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, object?>
                    {
                        ["granted"] = state.Value.Granted,
                        ["deleted"] = state.Value.Deleted,
                        ["reason"] = state.Value.Reason
                    },
                    cancellationToken);
                return;

            case ConsentResult.ForeignSubject:
                // Strict self-management: there is no delegation model, and
                // admin or guardian consent is a later, deliberate decision
                // rather than something allowed by omission.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "a consent belongs to its subject");
                return;

            case ConsentResult.Malformed malformed:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", malformed.Detail);
                return;
        }
    }

    /// <summary>
    /// The company the caller acts for, for the trail only.
    /// </summary>
    /// <remarks>
    /// It never decides anything here — a consent belongs to the person and
    /// follows them across employers, which is why the ledger has no tenant
    /// column (ADR-0017). It is recorded so an operator can see in what capacity
    /// somebody was acting, not so anyone can act for another.
    /// </remarks>
    private static TenantId? Company(Principal actor) =>
        actor.Acting is Capacity.ForCompany company ? company.Tenant : null;

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
