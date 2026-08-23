using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Nachrichten;
using WorkerTransfer.Jobs.Domain.Stellen;

namespace WorkerTransfer.Jobs.Application.Stellen;

/// <summary>Was eine Anzeige ausmacht, so wie ein Aufrufer sie schickt.</summary>
public sealed record Stellenangaben(
    string Titel,
    string Beschreibung,
    string Ort,
    Remotegrad Remote,
    Anstellungsart Art,
    IReadOnlyList<string> Faehigkeiten);

/// <summary>Schreibt eine neue Anzeige. Sie beginnt als Entwurf.</summary>
/// <remarks>
/// <c>Firma</c> kommt aus dem geprüften Token und nie aus dem Rumpf: eine
/// Mandantenkennung auf der Leitung wäre ein Weg, für ein fremdes Unternehmen
/// zu schreiben.
/// </remarks>
public sealed record StelleAnlegenBefehl(TenantId Firma, Stellenangaben Angaben)
    : IBefehl<Stelle>;

/// <inheritdoc cref="StelleAnlegenBefehl" />
public sealed class StelleAnlegenHandler(IStellenspeicher speicher, TimeProvider uhr)
    : IRequestHandler<StelleAnlegenBefehl, Stelle>
{
    /// <inheritdoc />
    public async Task<Stelle> Handle(
        StelleAnlegenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var angaben = request.Angaben;

        var stelle = Stelle.Lege_an(
            request.Firma, angaben.Titel, angaben.Beschreibung, angaben.Ort,
            angaben.Remote, angaben.Art, Faehigkeitenliste.Aus(angaben.Faehigkeiten),
            uhr.GetUtcNow());

        await speicher.SichereAsync(stelle, cancellationToken);

        return stelle;
    }
}

/// <summary>Ändert eine Anzeige.</summary>
public sealed record StelleAendernBefehl(TenantId Firma, Guid Id, Stellenangaben Angaben)
    : IBefehl<Stelle?>;

/// <inheritdoc cref="StelleAendernBefehl" />
/// <remarks>
/// <c>null</c> heißt <em>gibt es nicht oder gehört einem anderen Unternehmen</em>,
/// und die Api muss beides gleich beantworten: ein Unterschied verriete, welche
/// Anzeigen es anderswo gibt.
/// </remarks>
public sealed class StelleAendernHandler(IStellenspeicher speicher, TimeProvider uhr)
    : IRequestHandler<StelleAendernBefehl, Stelle?>
{
    /// <inheritdoc />
    public async Task<Stelle?> Handle(
        StelleAendernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stelle = await speicher.HoleAsync(request.Id, cancellationToken);

        if (stelle is null || stelle.Firma != request.Firma)
        {
            return null;
        }

        var angaben = request.Angaben;

        stelle.Aendere(
            angaben.Titel, angaben.Beschreibung, angaben.Ort, angaben.Remote,
            angaben.Art, Faehigkeitenliste.Aus(angaben.Faehigkeiten), uhr.GetUtcNow());

        await speicher.SichereAsync(stelle, cancellationToken);

        return stelle;
    }
}

/// <summary>Veröffentlicht oder schließt eine Anzeige.</summary>
public sealed record StandAendernBefehl(TenantId Firma, Guid Id, Stellenstand Ziel)
    : IBefehl<Stelle?>;

/// <inheritdoc cref="StandAendernBefehl" />
public sealed class StandAendernHandler(IStellenspeicher speicher, TimeProvider uhr)
    : IRequestHandler<StandAendernBefehl, Stelle?>
{
    /// <inheritdoc />
    public async Task<Stelle?> Handle(
        StandAendernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stelle = await speicher.HoleAsync(request.Id, cancellationToken);

        if (stelle is null || stelle.Firma != request.Firma)
        {
            return null;
        }

        var jetzt = uhr.GetUtcNow();

        if (request.Ziel is Stellenstand.Published)
        {
            stelle.Veroeffentliche(jetzt);
        }
        else
        {
            stelle.Schliesse(jetzt);
        }

        await speicher.SichereAsync(stelle, cancellationToken);

        return stelle;
    }
}
