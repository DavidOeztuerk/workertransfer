using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Die Funde einer Person — Namen, nie der Wortlaut.</summary>
/// <remarks>
/// <strong>Zwei Fragen und keine dritte.</strong> „Was steht in den Unterlagen
/// dieses Menschen" und „nimm den Fund zu dieser Datei weg". Eine Frage nach
/// dem <em>Wort</em> — welche Menschen tragen „Schweißfachmann" in ihren
/// Unterlagen — gibt es nicht und darf es nicht geben: durchsuchbar ist allein,
/// was jemand selbst in sein Profile getippt hat (ADR-0033). Deshalb liegt die
/// Wortliste auch als Feld in der Zeile und nicht als eigene Tabelle, in der
/// sich nach ihr suchen liesse.
/// </remarks>
public sealed class EfTermStore(ResumeDbContext context) : ITermStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentTerms>> AllAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Funde
            .AsNoTracking()
            .Where(row => row.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        DocumentTerms term, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(term);

        // `AsTracking()` IST DER SCHREIBVORGANG. Der ganze Kontext faehrt
        // `QueryTrackingBehavior.NoTracking`; ohne diese Zeile kaeme die Zeile
        // ABGELOEST zurueck, die Zuweisungen unten liefen ins Leere, und der
        // zweite Klick auf „lies meine Unterlagen" schriebe lautlos nichts —
        // gemessen am 09.09.2026 an einem anderen Speicher in diesem Dienst.
        var vorhanden = await context.Funde
            .AsTracking()
            .FirstOrDefaultAsync(
                row => row.SubjectId == term.Wer.Value
                    && row.DocumentId == term.DocumentRecord,
                cancellationToken);

        if (vorhanden is null)
        {
            vorhanden = new TermRow { Id = term.Id };
            context.Funde.Add(vorhanden);
        }

        vorhanden.SubjectId = term.Wer.Value;
        vorhanden.DocumentId = term.DocumentRecord;
        vorhanden.HasText = term.TextGefunden;
        vorhanden.Terms = JsonSerializer.Serialize(term.Begriffe, Gestalt);
        vorhanden.ReadAt = term.Gelesen.UtcDateTime;
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        SubjectId subject, Guid document, CancellationToken cancellationToken = default) =>
        context.Funde
            .Where(row => row.SubjectId == subject.Value && row.DocumentId == document)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>Wie die Wortliste in die Spalte kommt.</summary>
    /// <remarks>
    /// Ohne <c>UnsafeRelaxedJsonEscaping</c> würde „C#" als <c>C#</c>
    /// abgelegt — lesbar für den Serialisierer, unlesbar für jeden, der die
    /// Spalte ansieht. Es ist hier ungefährlich: nichts davon wird je als HTML
    /// ausgeliefert.
    /// </remarks>
    private static readonly JsonSerializerOptions Gestalt = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static DocumentTerms ZumAggregat(TermRow row) =>
        DocumentTerms.Stelle_her(
            row.Id,
            new SubjectId(row.SubjectId),
            row.DocumentId,
            row.HasText,
            JsonSerializer.Deserialize<string[]>(row.Terms) ?? [],
            new DateTimeOffset(row.ReadAt, TimeSpan.Zero));
}
