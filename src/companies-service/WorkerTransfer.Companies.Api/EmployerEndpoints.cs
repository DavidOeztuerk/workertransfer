using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Api.Authorization;
using WorkerTransfer.Companies.Application.Profiles;
using WorkerTransfer.Companies.Contracts;
using WorkerTransfer.Companies.Domain.EmployerProfiles;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.Companies.Api;

/// <summary>The four routes of the employer profile.</summary>
/// <remarks>
/// Three of them need no sign-in, and that is the purpose: an employer profile
/// is how a company presents itself, so putting it behind a sign-in would be
/// the opposite — exactly as with the job adverts.
/// </remarks>
public static class EmployerEndpoints
{
    /// <summary>Maps all routes of this service.</summary>
    public static IEndpointRouteBuilder MapEmployerEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var companies = app.MapGroup("/companies");

        companies.MapPut("/me/profile", async (
            WriteEmployerProfileV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            try
            {
                var profile = await mediator.Send(
                    new SaveProfileCommand(
                        company, body.DisplayName, body.About, body.Website,
                        body.Locations, body.Benefits,
                        body.Line1, body.PostalCode, body.City, body.Country, body.Phone),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Response(profile), cancellationToken);
            }
            catch (ProfileError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        })
        // The shop window stands publicly and speaks for every member — writing
        // it therefore demands an `admin`. Resolved from identity-service's
        // membership table, never from the token.
        .RequireAuthorization(
            AdminPermissionExtensions.Richtlinie(ShopWindowPermissions.Write));

        // `null` instead of 404: "none yet" is a state, not an error. The
        // interface shows an empty form for it.
        companies.MapGet("/me/profile", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            var profile = await mediator.Send(new MyProfileQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                profile is null ? null : Response(profile), cancellationToken);
        });

        // The order relative to the two routes below does not matter here —
        // measured, not assumed: `{tenantId:guid}` carries a constraint that
        // `by-slug` can never get past. The Python service had to pull this
        // route forward because FastAPI matches by declaration order; copying
        // that reason along would mean leaving a sentence standing that is
        // wrong here.
        companies.MapGet("/by-slug/{slug}", async (
            string slug,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var profile = await mediator.Send(
                new ProfileBySlugQuery(slug), cancellationToken);

            await Answer(context, profile, missingIs404: true, cancellationToken);
        });

        // Public, without a sign-in. 404 for as long as nothing was created —
        // then a company stays anonymous. Forcing a profile before anybody may
        // advertise would be a coupling between two services for a rule nobody
        // asked for.
        companies.MapGet("/{tenantId:guid}/profile", async (
            Guid tenantId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var profile = await mediator.Send(
                new PublicProfileQuery(new TenantId(tenantId)), cancellationToken);

            await Answer(context, profile, missingIs404: false, cancellationToken);
        });

        return app;
    }

    /// <summary>The caller's company, or a refusal already written.</summary>
    private static async Task<TenantId?> Company(HttpContext context, ICurrentPrincipal principal)
    {
        if (principal.Current is not { } actor)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated");

            return null;
        }

        if (actor.Acting is not Capacity.ForCompany company)
        {
            // A statement about the caller, not about somebody else's company:
            // a private person knows that they are not one.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "editing a company profile requires an active company");

            return null;
        }

        return company.Tenant;
    }

    private static async Task Answer(
        HttpContext context,
        EmployerProfile? profile,
        bool missingIs404,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            // Slug: the address itself asserts a company. Tenant id: the
            // company is anonymous, 200 null — otherwise /jobs logs every card.
            if (missingIs404)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such company profile");
                return;
            }

            await context.Response.WriteAsJsonAsync(profile, cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(Response(profile), cancellationToken);
    }

    private static EmployerProfileV1 Response(EmployerProfile profile) =>
        new(profile.Company.Value,
            profile.Slug,
            profile.DisplayName,
            profile.About,
            profile.Website,
            profile.Locations,
            profile.Benefits,
            profile.Line1,
            profile.PostalCode,
            profile.City,
            profile.Country,
            profile.Phone,
            profile.ChangedAt);
}
