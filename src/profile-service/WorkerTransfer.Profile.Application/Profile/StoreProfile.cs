using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Messages;
using WorkerTransfer.Profile.Domain.Audit;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Skills;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Create or change one's own profile.</summary>
/// <param name="Subject">The person — from the token, never from the body.</param>
/// <remarks>
/// One command for both. Separate commands would need a caller that knows
/// whether one exists already — and the interface shows the same form.
/// <para>
/// No field for visibility, and at no place of this path: it stands in the
/// consent ledger (ADR-0020).
/// </para>
/// </remarks>
public sealed record StoreProfileCommand(
    SubjectId Subject,
    string Headline,
    string Text,
    string Location,
    bool RemoteOk,
    IReadOnlyList<string> Skills,
    /// <summary>A stage, or <c>null</c> for "said nothing" (ADR-0041).</summary>
    CommuteReadiness? Commute = null,
    RelocationReadiness? Relocation = null) : ICommandMessage<PersonProfile>;

/// <summary>Writes the profile and records the write in the trail.</summary>
public sealed class StoreProfileHandler(
    IProfileStore store,
    IAuditTrail trail,
    TimeProvider clock) : IRequestHandler<StoreProfileCommand, PersonProfile>
{
    /// <inheritdoc />
    public async Task<PersonProfile> Handle(
        StoreProfileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();

        // The skills first, then the aggregate: both check, and whoever sends a
        // skill that is too long is to learn it before anything is written.
        var skills = SkillList.From(request.Skills);
        var existing = await store.GetAsync(request.Subject, cancellationToken);

        PersonProfile profile;

        if (existing is null)
        {
            profile = PersonProfile.Create(
                request.Subject, request.Headline, request.Text, request.Location,
                request.RemoteOk, skills, now,
                request.Commute, request.Relocation);
        }
        else
        {
            existing.Change(
                request.Headline, request.Text, request.Location,
                request.RemoteOk, skills, now,
                request.Commute, request.Relocation);
            profile = existing;
        }

        // Without this call the change would stay in memory: the aggregate
        // comes back detached from the store.
        await store.StoreAsync(profile, cancellationToken);

        await trail.AppendOneAsync(
            new AuditEntry(
                existing is null ? AuditAction.ProfileCreated : AuditAction.ProfileChanged,
                now,
                actor: request.Subject,
                target: request.Subject),
            cancellationToken);

        return profile;
    }
}
