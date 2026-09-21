using Noelia.Core.Identity;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>Ein Ledger, der aufschreibt statt zu fragen.</summary>
public sealed class Probeledger : IConsentGate
{
    /// <summary>Wessen Profile als freigegeben gilt. Leer heißt: niemandes.</summary>
    public HashSet<Guid> ProfilFrei { get; } = [];

    /// <summary>Welche (Person, Firma) den MarketStatus sehen darf.</summary>
    public HashSet<(Guid Wer, Guid Firma)> MarktFrei { get; } = [];

    /// <summary>Wenn gesetzt, schweigt der Ledger.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Was erteilt wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<(Guid Wer, Guid Firma)> Erteilt { get; } = [];

    /// <summary>Was widerrufen wurde.</summary>
    public List<(Guid Wer, Guid Firma)> Widerrufen { get; } = [];

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
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        Stumm();
        ArgumentNullException.ThrowIfNull(paare);

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. paare.Select(paar => MarktFrei.Contains((paar.Wer.Value, paar.Firma.Value)))]);
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

/// <summary>Eine Zustellung, die nichts tut — die Outbox-Zeile ist der Prüfstein.</summary>
public sealed class Probezustellung : IDelivery
{
    /// <inheritdoc />
    public Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
