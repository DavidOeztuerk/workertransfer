using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Messages;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Application.Mandates;

/// <summary>"Show me my mandate."</summary>
public sealed record MyMandateQuery(SubjectId Subject) : IQueryMessage<Mandate>;

/// <summary>Reads one's own mandate — without asking the ledger.</summary>
/// <remarks>
/// Checking one's own consent in order to see oneself would not only be a
/// pointless round trip: somebody who released nothing could otherwise no
/// longer edit their mandate (ADR-0020 §5).
/// <para>
/// Never <c>null</c>: "nothing said" <em>is</em> a state. A <c>null</c> would
/// force the UI to invent a default — and the danger is that it invents the
/// wrong one.
/// </para>
/// </remarks>
public sealed class MyMandateHandler(IMandateStore store, TimeProvider clock)
    : IRequestHandler<MyMandateQuery, Mandate>
{
    /// <inheritdoc />
    public async Task<Mandate> Handle(
        MyMandateQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await store.GetAsync(request.Subject, cancellationToken)
               ?? Mandate.Empty(request.Subject, clock.GetUtcNow());
    }
}

/// <summary>"Write my mandate."</summary>
public sealed record WriteMandateCommand(
    SubjectId Subject,
    string? EntryMonth,
    int? SalaryMin,
    int? SalaryMax,
    int? WorkloadPercent,
    IReadOnlyList<string>? ExcludedDomains) : ICommandMessage<Mandate>;

/// <summary>
/// Writes the four values — and gives up <c>profile.visibility:public</c> as
/// soon as a company is excluded.
/// </summary>
/// <remarks>
/// <para><strong>This is the line the acceptance hangs on.</strong> "The
/// current employer does not see their own staff in the scout" is not kept by a
/// second gate in the scout — that would be a second place deciding about
/// visibility — but in the ledger itself.</para>
///
/// <para>The ledger knows <strong>no denial</strong>. "Visible to everyone, but
/// not to X" cannot be expressed in it, and ADR-0037 therefore says: as long as
/// that is so, the mode "everyone" does not exist. Whoever names an exclusion
/// thus gives up "for all companies". Afterwards the person is visible only to
/// companies they released to one by one — and the excluded one is not among
/// them.</para>
///
/// <para><strong>The revocation goes through the ledger, not around it.</strong>
/// It is written with the person's token; the ledger is strictly
/// self-administered, so this step can only succeed when the person triggers it
/// themselves. If the ledger is silent, <em>nothing</em> is saved: a mandate
/// with an exclusion beside a profile that is still public would be the
/// half-here state in which the promise only looks kept.</para>
/// </remarks>
public sealed class WriteMandateHandler(
    IMandateStore store, IConsentGate gate, TimeProvider clock)
    : IRequestHandler<WriteMandateCommand, Mandate>
{
    /// <summary>What stands in the ledger as the reason. A shape, never a sentence about anyone.</summary>
    public const string RevocationReason = "an excluded company was named";

    /// <inheritdoc />
    /// <exception cref="InputError">An entry breaks the rule.</exception>
    public async Task<Mandate> Handle(
        WriteMandateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Check first, then write — and the check lives in the aggregate.
        var mandate = Mandate.Write(
            request.Subject,
            request.EntryMonth,
            request.SalaryMin,
            request.SalaryMax,
            request.WorkloadPercent,
            request.ExcludedDomains,
            clock.GetUtcNow());

        if (mandate.ExcludedDomains.Count > 0)
        {
            // BEFORE saving. If the ledger is silent it throws here — and the
            // mandate stays as it was. The other way round an exclusion would
            // stand in the table while the profile is still visible to all.
            await gate.RevokeAsync(
                [StageCapabilities.ProfilePublic], RevocationReason, cancellationToken);
        }

        await store.SaveAsync(mandate, cancellationToken);

        return mandate;
    }
}
