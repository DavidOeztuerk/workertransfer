using Girder.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Fragt die Mitglieder eines Unternehmens ab — für Benachrichtigungen.</summary>
/// <remarks>
/// Wird verwendet, wenn eine Bewerbung eingeht oder ein Entwurf gesendet wird.
/// Die Mitglieder werden über den internen Draht des identity-service geholt,
/// mit demselben gemeinsamen Geheimnis wie der Benachrichtigungsweg.
/// <para>
/// Der Port trägt Kennungen, nicht den Drahtvertrag: die Anwendungsschicht
/// kennt keine Serialisierung. Was über HTTP reist, mappt die Infrastruktur.
/// </para>
/// </remarks>
public interface IUnternehmensmitgliederAbfrage
{
    /// <summary>Alle Mitglieder eines Unternehmens.</summary>
    Task<IReadOnlyList<SubjectId>> HoleAsync(
        TenantId firma,
        CancellationToken cancellationToken = default);
}
