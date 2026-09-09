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
public sealed class EfPruefspur(ProfileDbContext context) : IPruefspur
{
    /// <inheritdoc />
    public Task HaengeAnAsync(Pruefeintrag eintrag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eintrag);

        context.Pruefeintraege.Add(new Pruefzeile
        {
            // Zeitgeordnet: die Spur wird nach Zeit gelesen, und eine
            // Zufallskennung streute die Einfügungen über den ganzen Index.
            Id = Guid.CreateVersion7(),
            AkteurId = eintrag.Akteur?.Value,
            FirmaId = eintrag.Firma?.Value,
            Handlung = eintrag.Handlung,
            BetroffenId = eintrag.Betroffen?.Value,
            Korrelation = eintrag.Korrelation,
            GeschehenAm = eintrag.GeschehenAm,
            Daten = JsonSerializer.Serialize(eintrag.Daten)
        });

        return Task.CompletedTask;
    }
}
