using Noelia.Core.Identity;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>Ein Ledger, der aufschreibt statt zu fragen.</summary>
public sealed class ProbeLedger : IConsentGate
{
    /// <summary>Whose profile counts as released. Empty means: nobody's.</summary>
    public HashSet<Guid> ProfilFrei { get; } = [];

    /// <summary>Welche (Person, Firma) den MarketStatus sehen darf.</summary>
    public HashSet<(Guid Subject, Guid Company)> MarktFrei { get; } = [];

    /// <summary>Wenn gesetzt, schweigt der Ledger.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Was erteilt wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<(Guid Subject, Guid Company)> Erteilt { get; } = [];

    /// <summary>Was widerrufen wurde.</summary>
    public List<(Guid Subject, Guid Company)> Widerrufen { get; } = [];

    /// <inheritdoc />
    public Task<bool> MaySeeProfileAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        Stumm();
        return Task.FromResult(ProfilFrei.Contains(subject.Value));
    }

    /// <inheritdoc />
    public Task<bool> MaySeeMarketAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        Stumm();
        return Task.FromResult(MarktFrei.Contains((subject.Value, company.Value)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> MayEachSeeMarketAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> paare,
        CancellationToken cancellationToken = default)
    {
        Stumm();
        ArgumentNullException.ThrowIfNull(paare);

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. paare.Select(pair => MarktFrei.Contains((pair.Subject.Value, pair.Company.Value)))]);
    }

    /// <inheritdoc />
    public Task GrantAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        Stumm();
        Erteilt.Add((subject.Value, company.Value));
        MarktFrei.Add((subject.Value, company.Value));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        Stumm();
        Widerrufen.Add((subject.Value, company.Value));
        MarktFrei.Remove((subject.Value, company.Value));
        return Task.CompletedTask;
    }

    private void Stumm()
    {
        if (Schweigt)
        {
            throw new ConsentGateSilent("Probe: der Ledger schweigt.");
        }
    }
}

/// <summary>A delivery that does nothing — the outbox row is the touchstone.</summary>
public sealed class Probezustellung : IDelivery
{
    /// <inheritdoc />
    public Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
