using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Application.Bewerbungen;

/// <summary>Die eigenen Bewerbungen.</summary>
public sealed record MeineBewerbungenAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Bewerbung>>;

/// <summary>Eine Bewerbung, wenn der Aufrufer sie sehen darf.</summary>
/// <remarks>
/// 404 für fremd und für nicht vorhanden bleibt ununterscheidbar. Die Mappe
/// des Unternehmens hängt an dieser einen Kennung; ohne sie müsste die
/// Oberfläche die Stelle kennen, bevor sie die Bewerbung öffnet.
/// </remarks>
public sealed record BewerbungLesenAbfrage(Guid Id, TenantId? Firma, SubjectId? Person)
    : IAbfrage<Bewerbung?>;

/// <summary>Die Bewerbungen auf eine Stelle dieses Unternehmens.</summary>
public sealed record BewerbungenZurStelleAbfrage(Guid Stelle, TenantId Firma)
    : IAbfrage<IReadOnlyList<Bewerbung>>;

/// <summary>Wie viele Bewerbungen dieses Unternehmen in welchem Stand hat.</summary>
/// <remarks>
/// Zahlen über die <em>eigenen</em> Vorgänge (ADR-0026). Die DoD verlangt
/// „Analytics aggregiert datenschutzkonform"; datenschutzkonform heißt hier
/// nicht „anonymisiert", sondern: es kommt keine Auskunft heraus, die der
/// Fragende nicht ohnehin hat. Das Unternehmen sieht seine Bewerbungen einzeln
/// in der Liste; die Summe darüber ist eine Bequemlichkeit, keine neue
/// Information.
/// <para>
/// Die Grenze verläuft nicht bei der Aggregation, sondern bei der
/// <strong>Zusammenführung</strong>: eine Zahl, die Bewerbungen mit
/// Marktstatus, Lebenslauf oder Vorgängen bei anderen Firmen verrechnet, wäre
/// eine Aussage über Menschen aus Quellen, die einzeln freigegeben wurden. So
/// etwas gibt es hier nicht und soll es nicht geben (ADR-0022/0026).
/// </para>
/// </remarks>
public sealed record FirmenzahlenAbfrage(TenantId Firma)
    : IAbfrage<IReadOnlyDictionary<Bewerbungsstand, int>>;

/// <summary>Beantwortet die drei Listenfragen.</summary>
/// <remarks>
/// Kein Consent-Aufruf, und das ist kein Versehen. Eine Bewerbung enthält keine
/// Profildaten; sie nennt eine <c>subject_id</c>. Wer mehr sehen will, fragt
/// die zuständigen Dienste, und dort greift der Ledger. Ihn hier zu fragen,
/// um zu zählen, was ohnehin sichtbar ist, wäre Theater.
/// </remarks>
public sealed class Bewerbungslisten(IBewerbungsspeicher speicher) :
    IRequestHandler<MeineBewerbungenAbfrage, IReadOnlyList<Bewerbung>>,
    IRequestHandler<BewerbungLesenAbfrage, Bewerbung?>,
    IRequestHandler<BewerbungenZurStelleAbfrage, IReadOnlyList<Bewerbung>>,
    IRequestHandler<FirmenzahlenAbfrage, IReadOnlyDictionary<Bewerbungsstand, int>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Bewerbung>> Handle(
        MeineBewerbungenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.FuerPersonAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Bewerbung?> Handle(
        BewerbungLesenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bewerbung = await speicher.HoleAsync(request.Id, cancellationToken);

        if (bewerbung is null)
        {
            return null;
        }

        if (request.Firma is { } firma && bewerbung.Firma == firma)
        {
            return bewerbung;
        }

        if (request.Person is { } person && bewerbung.Wer == person)
        {
            return bewerbung;
        }

        return null;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Bewerbung>> Handle(
        BewerbungenZurStelleAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Die Firma reist mit in die Abfrage, statt hinterher zu filtern:
        // eine fremde Stelle liefert nichts, statt zu verraten, dass es sie
        // gibt.
        return speicher.FuerStelleAsync(request.Stelle, request.Firma, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Bewerbungsstand, int>> Handle(
        FirmenzahlenAbfrage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.ZaehleAsync(request.Firma, cancellationToken);
    }
}
