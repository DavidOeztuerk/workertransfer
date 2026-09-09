using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Sessions;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Keeps what a sign-in acts as in <c>session_capacities</c>.</summary>
public sealed class EfSessionCapacity(IdentityDbContext context) : ISessionCapacity
{
    /// <inheritdoc />
    public async Task RememberAsync(
        SessionId session,
        Capacity capacity,
        CancellationToken cancellationToken = default)
    {
        var zeile = await Zeile(session, cancellationToken);

        if (capacity is not Capacity.ForCompany firma)
        {
            // Acting for oneself is what a missing row already says. Storing a
            // second way of saying it would mean two answers to one question.
            if (zeile is not null)
            {
                context.SessionCapacities.Remove(zeile);
            }

            return;
        }

        if (zeile is null)
        {
            context.SessionCapacities.Add(new SessionCapacityRow
            {
                SessionId = session.Value,
                TenantId = firma.Tenant.Value
            });

            return;
        }

        zeile.TenantId = firma.Tenant.Value;
        context.SessionCapacities.Update(zeile);
    }

    /// <inheritdoc />
    public async Task<Capacity> RecallAsync(
        SessionId session,
        CancellationToken cancellationToken = default)
    {
        var zeile = await Zeile(session, cancellationToken);

        return zeile is null
            ? Capacity.AsSelf.Instance
            : new Capacity.ForCompany(new TenantId(zeile.TenantId));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Goes through the change tracker rather than <c>ExecuteDeleteAsync</c>:
    /// the latter runs its own statement straight away, and the row would stay
    /// gone even if the command around it later failed.
    /// </remarks>
    public async Task ForgetAsync(SessionId session, CancellationToken cancellationToken = default)
    {
        var zeile = await Zeile(session, cancellationToken);

        if (zeile is not null)
        {
            context.SessionCapacities.Remove(zeile);
        }
    }

    private Task<SessionCapacityRow?> Zeile(
        SessionId session,
        CancellationToken cancellationToken) =>
        context.SessionCapacities
            .FirstOrDefaultAsync(row => row.SessionId == session.Value, cancellationToken);
}
