using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Identity.Application.Account;
using WorkerTransfer.Identity.Application.Companies;
using WorkerTransfer.Identity.Domain.Companies;
using Noelia.Infrastructure.Security.ServiceTokens;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>Internal service-to-service endpoints — each names its callers.</summary>
/// <remarks>
/// These endpoints have NO gateway route. Every list is a decision of ADR-0057:
/// widening one is a change to who can read what, and it is reviewed here, at
/// the door. Everyone not on a list gets the 404 of a door that is not there.
/// </remarks>
public static class InternalEndpoints
{
    /// <summary>Maps the internal routes onto the service's one door group.</summary>
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder doors)
    {
        ArgumentNullException.ThrowIfNull(doors);

        doors.MapGet("/companies/{tenantId:guid}/members", async (
            Guid tenantId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var members = await mediator.Send(
                new InternalMembersQuery(new TenantId(tenantId)),
                cancellationToken);

            var response = new CompanyMembersResponseV1(
                [.. members.Select(m => new CompanyMemberV1(m.Subject.Value))]);

            await context.Response.WriteAsJsonAsync(response, cancellationToken);
        }).RequireServiceCaller(
            ServiceClients.Applications, ServiceClients.Messages, ServiceClients.Transfer);

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
        doors.MapGet("/companies/{tenantId:guid}/members/{subjectId:guid}/role", async (
            Guid tenantId,
            Guid subjectId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var role = await mediator.Send(
                new InternalRoleQuery(new TenantId(tenantId), new SubjectId(subjectId)),
                cancellationToken);

            // 200 with `null` and not 404: "not a member" is an answer to the
            // question that was asked. A 404 would be indistinguishable from
            // a refused caller — and the caller could not tell the two apart at
            // exactly the place where that is the difference between "may not"
            // and "we do not know".
            await context.Response.WriteAsJsonAsync(
                new CompanyRoleV1(role is { } r ? MembershipRoleNames.ToDatabase(r) : null),
                cancellationToken);
        }).RequireServiceCaller(
            ServiceClients.Advisor, ServiceClients.Companies, ServiceClients.Jobs,
            ServiceClients.Messages, ServiceClients.Resume, ServiceClients.Transfer);

        // Who a company IS — name and proven domain, not its workforce.
        // advisor-service needs the domain so an exclusion can bite (ADR-0037):
        // a person names the companies no conversation comes about with, and
        // they name them by their domain.
        //
        // A second domain table in advisor-service would be the one that goes
        // stale first — with an exclusion that then silently stops biting, and
        // that is the one case where silence costs most.
        doors.MapGet("/companies/{tenantId:guid}", async (
            Guid tenantId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
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
        }).RequireServiceCaller(
            ServiceClients.Advisor, ServiceClients.Jobs, ServiceClients.Transfer);

        // The civil name and contact — the most sensitive of these doors.
        //
        // THE LIST IS NOT THE WHOLE PERMISSION HERE. It proves that this service
        // is asking, not that it may ask about this person. That permission
        // stands in the ledger,
        // and advisor-service fetches it BEFORE this call: stage 3 is
        // `advisor.identity:tenant:<id>` (ADR-0037). transfer-service asks
        // `employer.route:tenant:<id>` first and hands the name only to the
        // employer she named (ADR-0050). Whoever uses this door for something
        // else fetches the check along with it.
        //
        // Two fields, no postal address, no telephone (ADR-0038) — and no list:
        // it answers on exactly one id.
        doors.MapGet("/account/{subjectId:guid}/identity", async (
            Guid subjectId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
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
        }).RequireServiceCaller(
            ServiceClients.Advisor, ServiceClients.Transfer);

        doors.MapGet("/account/{subjectId:guid}/ai", async (
            Guid subjectId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var aiAccess = await mediator.Send(
                new InternalAiAccessQuery(new SubjectId(subjectId)),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new AiAccessV1(aiAccess.Provider, aiAccess.Address, aiAccess.Model, aiAccess.Key),
                cancellationToken);
        }).RequireServiceCaller(
            ServiceClients.Applications, ServiceClients.Messages, ServiceClients.Profile);

        // The company's twin: which model writes for the company's seams
        // (advert help, scout approach, company replies). The tenant comes from
        // the asking service's verified token, never from a body.
        doors.MapGet("/companies/{tenantId:guid}/ai", async (
            Guid tenantId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var aiAccess = await mediator.Send(
                new InternalCompanyAiAccessQuery(new TenantId(tenantId)),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new AiAccessV1(aiAccess.Provider, aiAccess.Address, aiAccess.Model, aiAccess.Key),
                cancellationToken);
        }).RequireServiceCaller(
            ServiceClients.Jobs, ServiceClients.Messages, ServiceClients.Scout);

        return doors;
    }
}
