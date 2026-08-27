using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Nachrichten;
using WorkerTransfer.Jobs.Domain.Stellen;

namespace WorkerTransfer.Jobs.Application.Rueckzug;

/// <summary>Zieht die Anzeigen eines Unternehmens zurück.</summary>
/// <remarks>
/// Der Sonderfall aus ADR-0027 §7: die letzte Person mit <c>role='admin'</c>
/// hat ihr Konto gelöscht. Eine Absicht über ein <b>Unternehmen</b>, nicht über
/// einen Menschen — und sie zählt ausdrücklich <em>nicht</em> in den
/// Vollständigkeitsnachweis der Löschung, sonst hielte ein stiller jobs-service
/// die Löschung eines Menschen offen.
/// </remarks>
public sealed record AnzeigenZurueckziehenBefehl(TenantId Firma) : IBefehl<int>;

/// <inheritdoc cref="AnzeigenZurueckziehenBefehl" />
public sealed class AnzeigenZurueckziehenHandler(IStellenspeicher speicher, TimeProvider uhr)
    : IRequestHandler<AnzeigenZurueckziehenBefehl, int>
{
    /// <inheritdoc />
    public Task<int> Handle(
        AnzeigenZurueckziehenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Zurückgezogen, nicht gelöscht: ein Unternehmen ist keine natürliche
        // Person, und seine Anzeigen gehören ihm auch dann noch. Aber eine
        // unbeaufsichtigte Stellenanzeige ist schlechter als keine —
        // Bewerbungen liefen an niemanden.
        return speicher.ZieheZurueckAsync(request.Firma, uhr.GetUtcNow(), cancellationToken);
    }
}
