using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Messages;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>One's own profile — without asking the ledger.</summary>
/// <param name="Subject">The person from the token.</param>
/// <remarks>
/// Checking one's own consent in order to see oneself would not only be a
/// superfluous round trip but wrong: whoever released nothing could otherwise
/// no longer edit their profile (ADR-0020 §5).
/// </remarks>
public sealed record MyProfileQuery(SubjectId Subject) : IQueryMessage<PersonProfile?>;

/// <summary>Reads one's own profile, or gives <c>null</c>.</summary>
/// <remarks>
/// <c>null</c> is no error here: "none created yet" is a state the interface
/// shows as an empty form.
/// </remarks>
public sealed class MyProfileHandler(IProfileStore store)
    : IRequestHandler<MyProfileQuery, PersonProfile?>
{
    /// <inheritdoc />
    public Task<PersonProfile?> Handle(MyProfileQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Subject, cancellationToken);
    }
}
