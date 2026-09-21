using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Application.Messages;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Application.Profiles;

/// <summary>The company's own profile.</summary>
/// <remarks>
/// Without a result type: "none created yet" is a state, not an error, and the
/// interface shows an empty form for it.
/// </remarks>
public sealed record MyProfileQuery(TenantId Company) : IQueryMessage<EmployerProfile?>;

/// <summary>A company's public profile.</summary>
/// <remarks>
/// Unlike the company's own, "none yet" here is not a form but a company that
/// stays anonymous — the endpoint answers 404. Forcing a profile before
/// anybody may advertise would be a coupling between two services for a rule
/// nobody asked for.
/// </remarks>
public sealed record PublicProfileQuery(TenantId Company) : IQueryMessage<EmployerProfile?>;

/// <summary>The careers page behind a slug.</summary>
public sealed record ProfileBySlugQuery(string Slug) : IQueryMessage<EmployerProfile?>;

/// <summary>Answers the three reading questions.</summary>
/// <remarks>
/// No consent call, and that is no oversight: an employer profile is a
/// statement by the company about itself, and there is nobody who could
/// consent. Making it public is its purpose; putting it behind a sign-in would
/// be the opposite — exactly as with the job adverts.
/// </remarks>
public sealed class ProfileQueries(IProfileStore store) :
    IRequestHandler<MyProfileQuery, EmployerProfile?>,
    IRequestHandler<PublicProfileQuery, EmployerProfile?>,
    IRequestHandler<ProfileBySlugQuery, EmployerProfile?>
{
    /// <inheritdoc />
    public Task<EmployerProfile?> Handle(
        MyProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Company, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmployerProfile?> Handle(
        PublicProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Company, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmployerProfile?> Handle(
        ProfileBySlugQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Slug, cancellationToken);
    }
}
