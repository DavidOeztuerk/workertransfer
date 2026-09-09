using Girder.Core.Identity;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>Ein Ledger, der aufschreibt statt zu fragen.</summary>
public sealed class Probeledger : IEinwilligungstor
{
    /// <summary>Wessen Profil als freigegeben gilt. Leer heißt: niemandes.</summary>
    public HashSet<Guid> ProfilFrei { get; } = [];

    /// <summary>Welche (Person, Firma) den Marktstatus sehen darf.</summary>
    public HashSet<(Guid Wer, Guid Firma)> MarktFrei { get; } = [];

    /// <summary>Wenn gesetzt, schweigt der Ledger.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Was erteilt wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<(Guid Wer, Guid Firma)> Erteilt { get; } = [];

    /// <summary>Was widerrufen wurde.</summary>
    public List<(Guid Wer, Guid Firma)> Widerrufen { get; } = [];

    /// <inheritdoc />
    public Task<bool> DarfProfilSehenAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        Stumm();
        return Task.FromResult(ProfilFrei.Contains(wer.Value));
    }

    /// <inheritdoc />
    public Task<bool> DarfMarktSehenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        Stumm();
        return Task.FromResult(MarktFrei.Contains((wer.Value, firma.Value)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> DuerfenMarktSehenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        Stumm();
        ArgumentNullException.ThrowIfNull(paare);

        return Task.FromResult<IReadOnlyList<bool>>(
            [.. paare.Select(paar => MarktFrei.Contains((paar.Wer.Value, paar.Firma.Value)))]);
    }

    /// <inheritdoc />
    public Task ErteileAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        Stumm();
        Erteilt.Add((wer.Value, firma.Value));
        MarktFrei.Add((wer.Value, firma.Value));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WiderrufeAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        Stumm();
        Widerrufen.Add((wer.Value, firma.Value));
        MarktFrei.Remove((wer.Value, firma.Value));
        return Task.CompletedTask;
    }

    private void Stumm()
    {
        if (Schweigt)
        {
            throw new EinwilligungSchweigt("Probe: der Ledger schweigt.");
        }
    }
}

/// <summary>Eine Zustellung, die nichts tut — die Outbox-Zeile ist der Prüfstein.</summary>
public sealed class Probezustellung : IZustellung
{
    /// <inheritdoc />
    public Task ZustelleAsync(
        SubjectId empfaenger, string art, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
