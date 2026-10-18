using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Draft;
using WorkerTransfer.Profile.Application.Erasure;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Domain;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary>What somebody sends to write their profile.</summary>
/// <remarks>
/// Without <c>subject_id</c>: whose profile this is stands in the verified
/// token. And <b>without any visibility field</b> — whether a profile may be
/// shown stands exclusively in the consent ledger (ADR-0020). A field here would
/// be a second truth, and the two would disagree at the first revocation.
/// </remarks>
/// <remarks>
/// <para><strong><c>remote_ok</c> stands there expressly, and here the damage was
/// silent.</strong> Without <c>[JsonPropertyName]</c> <c>RemoteOk</c> bound to
/// <c>remoteOk</c>, while the interface sends <c>remote_ok</c>
/// (<c>apps/web/src/profile/client.ts:98</c>). A <c>bool</c> has no not-null
/// lock and no guard: it simply fell back to <c>false</c>.</para>
///
/// <para>Whoever ticked "remote possible" and saved got <c>200</c> — and the
/// tick was gone. That is the more unpleasant half of the same error: the
/// registration at least failed loudly.</para>
/// </remarks>
public sealed record ProfileBody(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    /// <summary>A stage, or <c>null</c> for "said nothing" (ADR-0041).</summary>
    /// <remarks>
    /// <c>null</c> is a valid value and means something: the statement is
    /// thereby <em>taken back</em>. A "missing means: leave it as it was" would
    /// make taking it back impossible — and a statement one cannot take back is
    /// no voluntary one.
    /// </remarks>
    [property: JsonPropertyName("commute_km")] string? CommuteKm = null,
    [property: JsonPropertyName("relocation")] string? Relocation = null,
    /// <summary>Her own XING profile — names her; <c>null</c> takes it back.</summary>
    [property: JsonPropertyName("xing_url")] string? XingUrl = null);

/// <summary>What somebody sends to get help with phrasing.</summary>
public sealed record DraftBody(string Wish);

/// <summary><c>/profiles</c> — and since 11.09.2026 nothing beside it.</summary>
/// <remarks>
/// <c>GET /candidates</c> stood here and fell: scout-service is its successor,
/// and two searches side by side would be two truths (ADR-0036 decision 1).
/// What is needed of it now lies behind <c>/internal/profiles/search</c> —
/// behind the shared secret, without a gateway route, and without a ledger
/// check, because that stands one level higher.
/// </remarks>
public static class ProfileEndpoints
{
    /// <summary>Maps the profile routes.</summary>
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A silent ledger is neither a yes nor a no. 404 would say the person is
        // not here; handing out the profile would mean giving away something
        // nobody released. 503 says the only true thing: this service cannot
        // answer right now.
        //
        // Here as a filter instead of a catch in every route, because the route
        // that forgets it looks like a working one until the ledger fails.
        var profile = app.MapGroup("/profiles").AddEndpointFilter(CatchSilence);

        profile.MapPut("/me", async (
            ProfileBody body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            try
            {
                var profile = await mediator.Send(
                    new StoreProfileCommand(
                        caller.Subject, body.Headline, body.Bio,
                        body.Location, body.RemoteOk, body.Skills ?? [],
                        CommuteTerms.Read(body.CommuteKm),
                        RelocationTerms.Read(body.Relocation),
                        body.XingUrl),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(ToWire(profile), cancellationToken);
            }
            catch (InputError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        profile.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            // One's own profile without asking the ledger: the release governs
            // who sees it from outside, not whether somebody may read their own.
            var profile = await mediator.Send(
                new MyProfileQuery(caller.Subject), cancellationToken);

            // 200 null, not 404: "none yet" is the normal case, and the browser
            // turns every 404 into a console line.
            await context.Response.WriteAsJsonAsync(
                profile is null ? null : ToWire(profile), cancellationToken);
        });

        profile.MapPost("/me/draft", async (
            DraftBody body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            try
            {
                // The draft lives in the form until the person saves it — and
                // then it is their text. Nothing of it is put down here.
                var text = await mediator.Send(
                    new DraftQuery(caller.Subject, body.Wish ?? string.Empty),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["draft"] = text }, cancellationToken);
            }
            catch (DraftUnavailable error)
            {
                // The kind of the failure, never its content — and expressly no
                // template that would look like a suggestion.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", error.Message);
            }
            catch (InputError error)
            {
                // A wish that is too long is an input, not an outage: 422, not
                // 503. Without this branch it would leave the service as a 500 —
                // and a 500 tells the caller it is not their fault.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        profile.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            if (caller.Acting is not Capacity.ForCompany company)
            {
                // 403 is a statement about the caller and betrays nothing about
                // the person they ask about.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var profile = await mediator.Send(
                new ForeignProfileQuery(new SubjectId(subjectId), company.Tenant),
                cancellationToken);

            if (profile is null)
            {
                // Hidden and not present answer the same, and that must stay
                // so: a difference would say whether this human being is here.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such profile");
                return;
            }

            await context.Response.WriteAsJsonAsync(ToWire(profile), cancellationToken);
        });

        return app;
    }

    /// <summary>
    /// What a filter returns that has written the answer itself.
    /// </summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — JSON <c>null</c> with headers, and those
    /// stand already at this point. On a request without a body the caller
    /// still sees their 503 and only the log carries an unhandled exception; on
    /// one with a body the connection tears, and they get "Error while copying
    /// content to a stream". Measured at <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult Written = Results.Empty;

    private static async ValueTask<object?> CatchSilence(
        EndpointFilterInvocationContext call, EndpointFilterDelegate next)
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

            return Written;
        }
    }

    /// <remarks>
    /// Carries no visibility field, no points value, no rank and no percentage
    /// — and none belongs on it (ADR-0022).
    /// </remarks>
    private static Dictionary<string, object?> ToWire(PersonProfile profile) => new()
    {
        ["subject_id"] = profile.Subject.Value,
        ["headline"] = profile.Headline,
        ["bio"] = profile.Text,
        ["location"] = profile.Location,
        ["remote_ok"] = profile.RemoteOk,
        ["skills"] = profile.Skills.Values,
        // `null` travels along and is not left out: the interface must be able
        // to tell "said nothing" from "field does not exist".
        ["commute_km"] = CommuteTerms.Word(profile.Commute),
        ["relocation"] = RelocationTerms.Word(profile.Relocation),
        ["xing_url"] = profile.Xing,
        ["updated_at"] = profile.UpdatedAt
    };

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
