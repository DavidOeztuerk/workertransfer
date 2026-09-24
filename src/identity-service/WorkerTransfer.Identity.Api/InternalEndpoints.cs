using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Identity.Application.Account;
using WorkerTransfer.Identity.Application.Companies;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>Internal service-to-service endpoints — behind the shared secret.</summary>
/// <remarks>
/// These endpoints have NO gateway route. They live under `/internal/` and
/// behave like `/internal/notify` and `/internal/notifications`: without the
/// secret they answer 404 (not 401), so they do not give themselves away over a
/// method nobody uses.
/// </remarks>
public static class InternalEndpoints
{
    private const string SecretHeader = "X-Notify-Secret";

    /// <summary>Maps the internal routes.</summary>
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/internal/companies/{tenantId:guid}/members", async (
            Guid tenantId,
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayNotify(context, settings.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var members = await mediator.Send(
                new InternalMembersQuery(new TenantId(tenantId)),
                cancellationToken);

            var response = new CompanyMembersResponseV1(
                [.. members.Select(m => new CompanyMemberV1(m.Subject.Value))]);

            await context.Response.WriteAsJsonAsync(response, cancellationToken);
        });

        // A human being's role in a company — the wire over which the other
        // ten services answer a company permission.
        //
        // It is DELIBERATELY not in the token: a token lives on after somebody
        // was removed from a company, and the removal would then take effect
        // only on expiry. Here it is asked per request, and a revocation takes
        // effect at the next one.
        //
        // ONE question, ONE answer: the members list beside it would hand out
        // the whole workforce for every permission check, in order to read one
        // row out of it.
        app.MapGet("/internal/companies/{tenantId:guid}/members/{subjectId:guid}/role", async (
            Guid tenantId,
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayNotify(context, settings.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var role = await mediator.Send(
                new InternalRoleQuery(new TenantId(tenantId), new SubjectId(subjectId)),
                cancellationToken);

            // 200 with `null` and not 404: "not a member" is an answer to the
            // question that was asked. A 404 would be indistinguishable from
            // "wrong secret" — and the caller could not tell the two apart at
            // exactly the place where that is the difference between "may not"
            // and "we do not know".
            await context.Response.WriteAsJsonAsync(
                new CompanyRoleV1(role is { } r ? MembershipRoleNames.ToDatabase(r) : null),
                cancellationToken);
        });

        // Who a company IS — name and proven domain, not its workforce.
        // advisor-service needs the domain so an exclusion can bite (ADR-0037):
        // a person names the companies no conversation comes about with, and
        // they name them by their domain.
        //
        // A second domain table in advisor-service would be the one that goes
        // stale first — with an exclusion that then silently stops biting, and
        // that is the one case where silence costs most.
        app.MapGet("/internal/companies/{tenantId:guid}", async (
            Guid tenantId,
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayNotify(context, settings.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var company = await mediator.Send(
                new InternalCompanyQuery(new TenantId(tenantId)), cancellationToken);

            if (company is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                new CompanyLookupV1(company.Name, company.Domain.Value), cancellationToken);
        });

        // The civil name and contact — the most sensitive of these doors.
        //
        // THE SECRET IS NOT THE WHOLE PERMISSION HERE. It proves that a service
        // is asking, not that it may ask. The permission stands in the ledger,
        // and advisor-service fetches it BEFORE this call: stage 3 is
        // `advisor.identity:tenant:<id>` (ADR-0037). Whoever uses this door for
        // something else fetches the check along with it.
        //
        // Two fields, no postal address, no telephone (ADR-0038) — and no list:
        // it answers on exactly one id.
        app.MapGet("/internal/account/{subjectId:guid}/identity", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayNotify(context, settings.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var person = await mediator.Send(
                new InternalPersonQuery(new SubjectId(subjectId)), cancellationToken);

            if (person is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                new PersonLookupV1(person.Name, person.Email), cancellationToken);
        });

        app.MapGet("/internal/account/{subjectId:guid}/ai", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayNotify(context, settings.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var aiAccess = await mediator.Send(
                new InternalAiAccessQuery(new SubjectId(subjectId)),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new AiAccessV1(aiAccess.Provider, aiAccess.Address, aiAccess.Model, aiAccess.Key),
                cancellationToken);
        });

        return app;
    }

    private static bool MayNotify(HttpContext context, NotifySettings settings)
    {
        if (string.IsNullOrEmpty(settings.Geheimnis)
            || !context.Request.Headers.TryGetValue(SecretHeader, out var presented))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented.ToString()),
            System.Text.Encoding.UTF8.GetBytes(settings.Geheimnis));
    }
}
