using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Application.Messages;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Application.Profiles;

/// <summary>Writes the company's own employer profile.</summary>
/// <remarks>
/// <strong>Every member may do this</strong> — the same weighing as with the
/// job adverts. The role system knows two roles, and <c>admin</c> means
/// "manages the team"; content is the members' work.
/// </remarks>
public sealed record SaveProfileCommand(
    TenantId Company,
    string DisplayName,
    string About,
    string? Website,
    IReadOnlyList<string>? Locations,
    IReadOnlyList<string>? Benefits,
    string? Line1 = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null,
    string? Phone = null) : ICommandMessage<EmployerProfile>;

/// <summary>Creates or changes — and assigns the slug exactly once.</summary>
public sealed class SaveProfileHandler(IProfileStore store, TimeProvider clock)
    : IRequestHandler<SaveProfileCommand, EmployerProfile>
{
    /// <inheritdoc />
    public async Task<EmployerProfile> Handle(
        SaveProfileCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();
        var existing = await store.GetAsync(request.Company, cancellationToken);
        EmployerProfile profile;

        if (existing is null)
        {
            // The slug comes into being exactly once, at the first save. After
            // that it no longer follows the display name — otherwise every
            // shared link to the careers page would break.
            var slug = await store.FreeSlugAsync(
                Domain.EmployerProfiles.Slug.From(request.DisplayName), cancellationToken);

            profile = EmployerProfile.Create(
                request.Company, slug, request.DisplayName, request.About,
                request.Website, request.Locations, request.Benefits, now,
                request.Line1, request.PostalCode, request.City, request.Country,
                request.Phone);
        }
        else
        {
            existing.Change(
                request.DisplayName, request.About, request.Website,
                request.Locations, request.Benefits, now,
                request.Line1, request.PostalCode, request.City, request.Country,
                request.Phone);

            profile = existing;
        }

        await store.SaveAsync(profile, cancellationToken);

        return profile;
    }
}
