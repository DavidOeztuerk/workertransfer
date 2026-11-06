using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.Profile.Domain.Profile;
using Noelia.Infrastructure.Security.ServiceTokens;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary>Internal service-to-service endpoints — scout-service alone may call them.</summary>
/// <remarks>
/// <para>These endpoints have <strong>no gateway route</strong>. They lie under
/// <c>/internal/</c> and behave like <c>/internal/notify</c> and
/// <c>/internal/notifications</c>: to everyone not on the list they answer 404
/// (not 401), so they do not betray themselves through a method nobody uses.</para>
///
/// <para><strong>They do not ask the ledger, and that is the most delicate
/// sentence of this file.</strong> The caller is scout-service, and the release
/// check stands there — for the whole page at once, over <c>/check-batch</c>
/// (ADR-0030, ADR-0036 decision 1). Exactly ONE place decides about
/// visibility; two would be two truths. Whoever uses this door for something
/// else fetches the check along with it — or shows profiles nobody
/// released.</para>
///
/// <para>The query keys (<c>limit</c>, <c>cursor</c>, <c>skill</c>,
/// <c>location</c>, <c>remote</c>) are the contract with scout-service and are
/// read as strings on purpose.</para>
/// </remarks>
public static class InternalEndpoints
{
    /// <summary>Maps the internal routes.</summary>
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var doors = app.MapGroup("/internal");

        // The search for scout-service. Only over NAMED skills (ADR-0033):
        // profile-service knows no evidence, and this route is one reason to
        // leave it that way.
        doors.MapGet("/profiles/search", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query;

            var page = await mediator.Send(
                new InternalProfileSearchQuery(
                    PageLength(query["limit"]),
                    query["cursor"],
                    query["skill"].Count > 0 ? [.. query["skill"]!] : null,
                    query["location"].ToString(),
                    query["remote"] == "true"),
                cancellationToken);

            // No total — not even here, where only one service reads along: what
            // does not exist nobody can pass on by accident.
            await context.Response.WriteAsJsonAsync(
                new ProfileHitPageV1(
                    [.. page.Items.Select(Hit)],
                    page.Next?.Write()),
                cancellationToken);
        }).RequireServiceCaller(ServiceClients.Scout);

        // A single profile — for the draft of an approach.
        doors.MapGet("/profiles/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var profile = await mediator.Send(
                new MyProfileQuery(new SubjectId(subjectId)), cancellationToken);

            if (profile is null)
            {
                // 404 means "there is none" here. Whether it may be shown the
                // caller has asked already — there is nothing to say about that
                // here.
                await NotFound(context);
                return;
            }

            await context.Response.WriteAsJsonAsync(Hit(profile), cancellationToken);
        }).RequireServiceCaller(ServiceClients.Scout);

        doors.MapServiceFallback();

        return app;
    }

    private static ProfileHitV1 Hit(PersonProfile profile) =>
        new(
            profile.Subject.Value,
            profile.Headline,
            profile.Text,
            profile.Location,
            profile.RemoteOk,
            profile.Skills.Values,
            CommuteTerms.Word(profile.Commute),
            RelocationTerms.Word(profile.Relocation));

    /// <summary>How many rows a page carries.</summary>
    /// <remarks>
    /// A page length and never a count or a total (ADR-0026): the name is the
    /// guard <c>wt.ki.keine-zahl</c> reads.
    /// </remarks>
    private static int PageLength(string? raw) =>
        int.TryParse(raw, out var value)
        && value > 0
        && value <= InternalProfileSearchHandler.MaxPageLength
            ? value
            : InternalProfileSearchHandler.DefaultPageLength;

    private static Task NotFound(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
}
