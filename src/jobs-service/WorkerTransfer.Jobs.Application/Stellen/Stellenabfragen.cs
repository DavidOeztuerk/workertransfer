using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Nachrichten;
using WorkerTransfer.Jobs.Domain.Stellen;

namespace WorkerTransfer.Jobs.Application.Stellen;

/// <summary>Die Anzeigen des eigenen Unternehmens, alle Stände.</summary>
public sealed record MeineStellenAbfrage(TenantId Firma) : IAbfrage<IReadOnlyList<Stelle>>;

/// <inheritdoc cref="MeineStellenAbfrage" />
public sealed class MeineStellenHandler(IStellenspeicher speicher)
    : IRequestHandler<MeineStellenAbfrage, IReadOnlyList<Stelle>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Stelle>> Handle(
        MeineStellenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.FuerFirmaAsync(request.Firma, cancellationToken);
    }
}

/// <summary>Die öffentliche Liste.</summary>
/// <remarks>
/// Ohne Stand als Parameter: der wird im Speicher gefiltert. Ein
/// <c>status=draft</c> auf der Leitung wäre ein Weg, fremde Entwürfe zu lesen.
/// <para>
/// Und ohne jede Sortierung nach Passung. Wie gut jemand zu einer Stelle passt,
/// wird im Browser gerechnet und der <em>Person</em> gezeigt (ADR-0022).
/// </para>
/// </remarks>
public sealed record StellensucheAbfrage(
    int Anzahl,
    string? Zeiger,
    IReadOnlyList<string>? Faehigkeiten = null,
    string Ort = "",
    Remotegrad? Remote = null,
    string Suchbegriff = "",
    Guid? Firma = null,
    string Beschaeftigung = "") : IAbfrage<Stellenseite>;

/// <inheritdoc cref="StellensucheAbfrage" />
public sealed class StellensucheHandler(IStellenspeicher speicher)
    : IRequestHandler<StellensucheAbfrage, Stellenseite>
{
    /// <inheritdoc />
    public Task<Stellenseite> Handle(
        StellensucheAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.SucheAsync(
            request.Anzahl, request.Zeiger, request.Faehigkeiten,
            request.Ort, request.Remote, request.Suchbegriff, request.Firma,
            request.Beschaeftigung, cancellationToken);
    }
}

/// <summary>Eine einzelne Anzeige.</summary>
/// <param name="Firma">
/// Das Unternehmen des Aufrufers, oder <c>null</c> für eine Privatperson.
/// </param>
public sealed record StelleAbfrage(Guid Id, TenantId? Firma) : IAbfrage<Stelle?>;

/// <inheritdoc cref="StelleAbfrage" />
/// <remarks>
/// Ein Entwurf ist nur für sein eigenes Unternehmen sichtbar. Für alle anderen
/// antwortet dieser Dienst wie bei einer Anzeige, die es nicht gibt — sonst
/// verriete er, dass ein Unternehmen gerade etwas schreibt.
/// </remarks>
public sealed class StelleHandler(IStellenspeicher speicher)
    : IRequestHandler<StelleAbfrage, Stelle?>
{
    /// <inheritdoc />
    public async Task<Stelle?> Handle(StelleAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stelle = await speicher.HoleAsync(request.Id, cancellationToken);

        if (stelle is null)
        {
            return null;
        }

        var eigene = request.Firma is { } firma && stelle.Firma == firma;

        return eigene || stelle.Stand is Stellenstand.Published ? stelle : null;
    }
}
