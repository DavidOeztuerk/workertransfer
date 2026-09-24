using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Infrastructure.Internal;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary>Internal service-to-service endpoints — behind the shared secret.</summary>
/// <remarks>
/// <para>These endpoints have <strong>no gateway route</strong>. They lie under
/// <c>/internal/</c> and behave like <c>/internal/notify</c> and
/// <c>/internal/notifications</c>: without the secret they answer 404 (not
/// 401), so they do not betray themselves through a method nobody uses.</para>
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
    private const string SecretHeader = "X-Notify-Secret";

    /// <summary>Maps the internal routes.</summary>
    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The search for scout-service. Only over NAMED skills (ADR-0033):
        // profile-service knows no evidence, and this route is one reason to
        // leave it that way.
        app.MapGet("/internal/profiles/search", async (
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayAsk(context, settings.Value))
            {
                await NotFound(context);
                return;
            }

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
        });

        // A single profile — for the draft of an approach.
        app.MapGet("/internal/profiles/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayAsk(context, settings.Value))
            {
                await NotFound(context);
                return;
            }

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
        });

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

    private static bool MayAsk(HttpContext context, NotifySettings settings)
    {
        if (string.IsNullOrEmpty(settings.Secret)
            || !context.Request.Headers.TryGetValue(SecretHeader, out var presented))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented.ToString()),
            System.Text.Encoding.UTF8.GetBytes(settings.Secret));
    }
}
