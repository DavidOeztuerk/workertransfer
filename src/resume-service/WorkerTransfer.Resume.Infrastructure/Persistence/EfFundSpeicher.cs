using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Die Funde einer Person — Namen, nie der Wortlaut.</summary>
/// <remarks>
/// <strong>Zwei Fragen und keine dritte.</strong> „Was steht in den Unterlagen
/// dieses Menschen" und „nimm den Fund zu dieser Datei weg". Eine Frage nach
/// dem <em>Wort</em> — welche Menschen tragen „Schweißfachmann" in ihren
/// Unterlagen — gibt es nicht und darf es nicht geben: durchsuchbar ist allein,
/// was jemand selbst in sein Profil getippt hat (ADR-0033). Deshalb liegt die
/// Wortliste auch als Feld in der Zeile und nicht als eigene Tabelle, in der
/// sich nach ihr suchen liesse.
/// </remarks>
public sealed class EfFundSpeicher(ResumeDbContext kontext) : IFundSpeicher
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Unterlagenfund>> AlleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Funde
            .AsNoTracking()
            .Where(zeile => zeile.SubjectId == wer.Value)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Unterlagenfund fund, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fund);

        // `AsTracking()` IST DER SCHREIBVORGANG. Der ganze Kontext faehrt
        // `QueryTrackingBehavior.NoTracking`; ohne diese Zeile kaeme die Zeile
        // ABGELOEST zurueck, die Zuweisungen unten liefen ins Leere, und der
        // zweite Klick auf „lies meine Unterlagen" schriebe lautlos nichts —
        // gemessen am 09.09.2026 an einem anderen Speicher in diesem Dienst.
        var vorhanden = await kontext.Funde
            .AsTracking()
            .FirstOrDefaultAsync(
                zeile => zeile.SubjectId == fund.Wer.Value
                    && zeile.DocumentId == fund.Unterlage,
                cancellationToken);

        if (vorhanden is null)
        {
            vorhanden = new FundZeile { Id = fund.Id };
            kontext.Funde.Add(vorhanden);
        }

        vorhanden.SubjectId = fund.Wer.Value;
        vorhanden.DocumentId = fund.Unterlage;
        vorhanden.HasText = fund.TextGefunden;
        vorhanden.Terms = JsonSerializer.Serialize(fund.Begriffe, Gestalt);
        vorhanden.ReadAt = fund.Gelesen.UtcDateTime;
    }

    /// <inheritdoc />
    public Task LoescheAsync(
        SubjectId wer, Guid unterlage, CancellationToken cancellationToken = default) =>
        kontext.Funde
            .Where(zeile => zeile.SubjectId == wer.Value && zeile.DocumentId == unterlage)
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

    private static Unterlagenfund ZumAggregat(FundZeile zeile) =>
        Unterlagenfund.Stelle_her(
            zeile.Id,
            new SubjectId(zeile.SubjectId),
            zeile.DocumentId,
            zeile.HasText,
            JsonSerializer.Deserialize<string[]>(zeile.Terms) ?? [],
            new DateTimeOffset(zeile.ReadAt, TimeSpan.Zero));
}
