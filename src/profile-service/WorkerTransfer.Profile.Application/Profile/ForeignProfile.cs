using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Messages;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Another human being's profile.</summary>
/// <param name="Subject">Whose profile is looked for.</param>
/// <param name="Company">The caller's company — from the token, never from the request.</param>
public sealed record ForeignProfileQuery(SubjectId Subject, TenantId Company)
    : IQueryMessage<PersonProfile?>;

/// <summary>Reads a foreign profile if the ledger allows it.</summary>
/// <remarks>
/// <c>null</c> means "not present OR not released" — and the two must stay
/// indistinguishable from outside. An exit of its own for "exists but does not
/// show itself" would be an oracle: whoever tries a list of ids would learn who
/// is a member without ever seeing a profile (ADR-0020 §1).
/// </remarks>
public sealed class ForeignProfileHandler(IProfileStore store, IConsentGate gate)
    : IRequestHandler<ForeignProfileQuery, PersonProfile?>
{
    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    public async Task<PersonProfile?> Handle(
        ForeignProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = await store.GetAsync(request.Subject, cancellationToken);

        if (profile is null)
        {
            // No ledger call for a profile that does not exist: a superfluous
            // round trip, and it would report guessed ids to the ledger.
            return null;
        }

        // ConsentGateSilent flies through on purpose: the endpoint turns it into
        // 503. Turning it into false here would mean claiming the person did not
        // consent — we do not know that.
        return await gate.MaySeeAsync(request.Subject, request.Company, cancellationToken)
            ? profile
            : null;
    }
}
