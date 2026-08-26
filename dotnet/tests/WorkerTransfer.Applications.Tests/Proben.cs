using Girder.Core.Identity;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Ein Ledger, der aufschreibt statt zu fragen.</summary>
/// <remarks>
/// Er merkt sich, was hinausgegangen <em>wäre</em> — das ist bei diesem Dienst
/// die interessante Frage, denn was er in den Ledger schreibt, ist die Freigabe
/// selbst.
/// </remarks>
public sealed class Probeledger : IEinwilligungsschreiber
{
    /// <summary>Was erteilt wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<string> Erteilt { get; } = [];

    /// <summary>Was widerrufen wurde.</summary>
    public List<string> Widerrufen { get; } = [];

    /// <summary>Wenn gesetzt, schweigt der Ledger.</summary>
    public bool Schweigt { get; set; }

    /// <inheritdoc />
    public Task ErteileAsync(
        SubjectId wer,
        IReadOnlyList<string> faehigkeiten,
        CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new EinwilligungSchweigt("Probe: der Ledger schweigt.");
        }

        Erteilt.AddRange(faehigkeiten);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WiderrufeAsync(
        SubjectId wer,
        IReadOnlyList<string> faehigkeiten,
        CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new EinwilligungSchweigt("Probe: der Ledger schweigt.");
        }

        Widerrufen.AddRange(faehigkeiten);
        return Task.CompletedTask;
    }
}

/// <summary>Ein Jobs-Dienst, der antwortet, was der Test bestimmt.</summary>
public sealed class Probestellen : IStellenauskunft
{
    private readonly Dictionary<Guid, OeffentlicheStelle> _offen = [];

    /// <summary>Wenn gesetzt, schweigt der Dienst.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Legt eine öffentliche Stelle an und gibt ihre Kennung zurück.</summary>
    public Guid Oeffentlich(Guid firma)
    {
        var id = Guid.CreateVersion7();
        _offen[id] = new OeffentlicheStelle(id, new TenantId(firma), "Entwicklerin");
        return id;
    }

    /// <inheritdoc />
    public Task<OeffentlicheStelle?> HoleAsync(
        Guid stelle, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new StelleSchweigt("Probe: der Jobs-Dienst schweigt.");
        }

        return Task.FromResult(_offen.GetValueOrDefault(stelle));
    }
}

/// <summary>Eine Zustellung, die nichts tut — die Outbox-Zeile ist der Prüfstein.</summary>
public sealed class Probezustellung : WorkerTransfer.Outbox.IZustellung
{
    /// <inheritdoc />
    public Task ZustelleAsync(
        SubjectId empfaenger, string art, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
