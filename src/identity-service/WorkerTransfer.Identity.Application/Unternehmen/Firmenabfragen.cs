using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>Which companies may I act for?</summary>
public sealed record MeineUnternehmenAbfrage(SubjectId Wer) : IAbfrage<IReadOnlyList<Mitgliedschaft>>;

/// <inheritdoc cref="MeineUnternehmenAbfrage" />
public sealed class MeineUnternehmenHandler(IMembershipRepository mitgliedschaften)
    : IRequestHandler<MeineUnternehmenAbfrage, IReadOnlyList<Mitgliedschaft>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Mitgliedschaft>> Handle(
        MeineUnternehmenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return mitgliedschaften.ListForSubjectAsync(request.Wer, cancellationToken);
    }
}

/// <summary>Who may act for this company?</summary>
public sealed record MitgliederAbfrage(SubjectId Wer, TenantId Firma)
    : IAbfrage<IReadOnlyList<Firmenmitglied>>;

/// <inheritdoc cref="MitgliederAbfrage" />
/// <remarks>
/// Any member may read the list — it is the company's own team, and hiding
/// colleagues from colleagues serves nobody. Only membership is required, and
/// that check is what keeps it from being a directory of strangers.
/// </remarks>
public sealed class MitgliederHandler(
    Firmenzugriff zugriff,
    IMembershipRepository mitgliedschaften)
    : IRequestHandler<MitgliederAbfrage, IReadOnlyList<Firmenmitglied>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Firmenmitglied>> Handle(
        MitgliederAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await zugriff.RolleAsync(request.Wer, request.Firma, cancellationToken);

        return await mitgliedschaften.ListMembersAsync(request.Firma, cancellationToken);
    }
}

/// <summary>Which invitations are still waiting?</summary>
public sealed record OffeneEinladungenAbfrage(SubjectId Wer, TenantId Firma)
    : IAbfrage<IReadOnlyList<Invitation>>;

/// <inheritdoc cref="OffeneEinladungenAbfrage" />
public sealed class OffeneEinladungenHandler(
    Firmenzugriff zugriff,
    IInvitationRepository einladungen)
    : IRequestHandler<OffeneEinladungenAbfrage, IReadOnlyList<Invitation>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Invitation>> Handle(
        OffeneEinladungenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await zugriff.RolleAsync(request.Wer, request.Firma, cancellationToken);

        return await einladungen.ListOpenAsync(request.Firma, cancellationToken);
    }
}
