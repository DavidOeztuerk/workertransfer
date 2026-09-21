using Noelia.Core.Identity;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Ein Ledger, der aufschreibt statt zu fragen.</summary>
/// <remarks>
/// Er merkt sich, was hinausgegangen <em>wäre</em> — das ist bei diesem Dienst
/// die interessante Frage, denn was er in den Ledger schreibt, ist die Release
/// selbst.
/// </remarks>
public sealed class Probeledger : IConsentWriter
{
    /// <summary>Was erteilt wurde, in der Reihenfolge der Aufrufe.</summary>
    public List<string> Erteilt { get; } = [];

    /// <summary>Was widerrufen wurde.</summary>
    public List<string> Widerrufen { get; } = [];

    /// <summary>Wenn gesetzt, schweigt der Ledger.</summary>
    public bool Schweigt { get; set; }

    /// <inheritdoc />
    public Task GrantAsync(
        SubjectId subject,
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new ConsentGateSilent("Probe: der Ledger schweigt.");
        }

        Erteilt.AddRange(skills);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeAsync(
        SubjectId subject,
        IReadOnlyList<string> skills,
        CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new ConsentGateSilent("Probe: der Ledger schweigt.");
        }

        Widerrufen.AddRange(skills);
        return Task.CompletedTask;
    }
}

/// <summary>Eine Mitgliederabfrage, die antwortet, was der Test bestimmt.</summary>
public sealed class Probemitglieder : ICompanyMemberLookup
{
    private readonly Dictionary<TenantId, IReadOnlyList<SubjectId>> _mitglieder = [];

    /// <summary>Wenn gesetzt, schweigt der Dienst.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Legt Mitglieder für ein Unternehmen fest.</summary>
    public void Fuer(TenantId company, params SubjectId[] members)
    {
        _mitglieder[company] = members;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SubjectId>> GetAsync(
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new CompanyLookupSilent("Probe: der Identitaetsdienst schweigt.");
        }

        return Task.FromResult(_mitglieder.GetValueOrDefault(company, []));
    }
}

/// <summary>Ein Jobs-Dienst, der antwortet, was der Test bestimmt.</summary>
public sealed class Probestellen : IJobLookup
{
    private readonly Dictionary<Guid, PublicJob> _offen = [];

    /// <summary>Wenn gesetzt, schweigt der Dienst.</summary>
    public bool Schweigt { get; set; }

    /// <summary>Legt eine öffentliche Job an und gibt ihre Kennung zurück.</summary>
    public Guid Oeffentlich(Guid company)
    {
        var id = Guid.CreateVersion7();
        _offen[id] = new PublicJob(id, new TenantId(company), "Entwicklerin");
        return id;
    }

    /// <inheritdoc />
    public Task<PublicJob?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        if (Schweigt)
        {
            throw new JobLookupSilent("Probe: der Jobs-Dienst schweigt.");
        }

        return Task.FromResult(_offen.GetValueOrDefault(job));
    }
}

/// <summary>Ein KI-Zugang, den der Test bestimmt — Vorgabe: keiner.</summary>
public sealed class ProbeKiZugang : IKiZugangAbfrage
{
    /// <summary>Was zurückkommt. Ohne Setzen: kein Anbieter.</summary>
    public KiZugang Wert { get; set; } =
        new("none", string.Empty, string.Empty, string.Empty);

    /// <inheritdoc />
    public Task<KiZugang> GetAsync(
        Noelia.Core.Identity.SubjectId subject,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Wert);
}

/// <summary>Eine Zustellung, die nichts tut — die Outbox-Zeile ist der Prüfstein.</summary>
public sealed class Probezustellung : WorkerTransfer.Outbox.IDelivery
{
    /// <inheritdoc />
    public Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
