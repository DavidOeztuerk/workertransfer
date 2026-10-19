using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Jobs.Application.Withdrawal;
using WorkerTransfer.Jobs.Infrastructure.Withdrawal;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Api;

/// <summary><c>POST /companies/withdrawal</c>.</summary>
/// <remarks>
/// <b>No erasure endpoint.</b> This service holds nothing about a natural person
/// and is therefore no recipient of the cascade (ADR-0027 §2). What arrives here
/// is the intent from §7: the company has lost its last administrator, and its
/// adverts are withdrawn.
/// <para>
/// It expressly does <em>not</em> count towards the completeness evidence of the
/// erasure — otherwise a silent jobs-service would keep the erasure of a human
/// being open.
/// </para>
/// </remarks>
public static class WithdrawalEndpoints
{
    private const string SecretHeader = "X-Erasure-Secret";

    /// <summary>Maps the one endpoint the origin calls.</summary>
    public static IEndpointRouteBuilder MapWithdrawalEndpoint(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/companies/withdrawal", async (
            CompanyWithdrawalV1 body,
            IMediator mediator,
            IOptions<WithdrawalSettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayWithdraw(context, settings.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var withdrawn = await mediator.Send(
                new WithdrawAdvertsCommand(new TenantId(body.TenantId)),
                cancellationToken);

            // Withdrawn, not erased: a company is no natural person, and its
            // adverts still belong to it then.
            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, int> { ["withdrawn"] = withdrawn },
                cancellationToken);
        });

        return app;
    }

    private static bool MayWithdraw(
        HttpContext context, WithdrawalSettings settings)
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
