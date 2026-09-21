using System.Text.Json;
using WorkerTransfer.Profile.Domain.Pruefspur;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Hängt an <c>audit_events</c> an.</summary>
/// <remarks>
/// Legt in die Änderungsverfolgung und speichert <em>nicht</em>. Ein Speichern
/// hier gäbe jedem Eintrag eine eigene Transaktion — genau die Eigenschaft, die
/// die Spur nicht haben darf: der Eintrag schließt mit der Änderung ab, die er
/// festhält, oder er hält nichts fest.
/// </remarks>
public sealed class EfAuditTrail(ProfileDbContext context) : IAuditTrail
{
    /// <inheritdoc />
    public Task AppendOneAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.Pruefeintraege.Add(new AuditRow
        {
            // Zeitgeordnet: die Spur wird nach Zeit gelesen, und eine
            // Zufallskennung streute die Einfügungen über den ganzen Index.
            Id = Guid.CreateVersion7(),
            AkteurId = entry.Akteur?.Value,
            FirmaId = entry.Firma?.Value,
            Handlung = entry.Handlung,
            BetroffenId = entry.Betroffen?.Value,
            Korrelation = entry.Korrelation,
            GeschehenAm = entry.GeschehenAm,
            Daten = JsonSerializer.Serialize(entry.Daten)
        });

        return Task.CompletedTask;
    }
}
