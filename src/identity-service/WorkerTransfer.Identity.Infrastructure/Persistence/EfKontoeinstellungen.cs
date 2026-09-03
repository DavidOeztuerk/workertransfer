using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Die Einstellungen einer Person, in ihrer Zeile.</summary>
/// <remarks>
/// <strong>Wer nie etwas gesetzt hat, hat keine Zeile</strong> — und bekommt
/// die Vorgabe. Für jedes Konto beim Anlegen eine Zeile zu schreiben hiesse,
/// eine Entscheidung zu behaupten, die niemand getroffen hat; und die Vorgabe
/// steht ohnehin im Aggregat, wo man sie liest.
/// </remarks>
public sealed class EfKontoeinstellungen(IdentityDbContext kontext, TimeProvider uhr)
    : IKontoeinstellungen
{
    /// <inheritdoc />
    public async Task<Kontoeinstellungen> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.AccountSettings
            .FirstOrDefaultAsync(eintrag => eintrag.SubjectId == wer.Value, cancellationToken);

        return zeile is null
            ? Kontoeinstellungen.Vorgabe(wer)
            : Kontoeinstellungen.Wiederherstellen(
                wer,
                zeile.DeleteAfterMonths,
                Anbieter(zeile.AiProvider),
                zeile.AiBaseUrl,
                zeile.AiModel,
                zeile.AiKeyEncrypted,
                zeile.AiKeyTail,
                zeile.AiAuditLog);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Kontoeinstellungen einstellungen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(einstellungen);

        var jetzt = uhr.GetUtcNow().UtcDateTime;

        var zeile = await kontext.AccountSettings
            .AsTracking()
            .FirstOrDefaultAsync(
                eintrag => eintrag.SubjectId == einstellungen.Wer.Value, cancellationToken);

        if (zeile is null)
        {
            zeile = new KontoeinstellungenRow
            {
                SubjectId = einstellungen.Wer.Value,
                CreatedAt = jetzt
            };
            kontext.AccountSettings.Add(zeile);
        }

        zeile.DeleteAfterMonths = einstellungen.LoeschungNachMonaten;
        zeile.AiProvider = Etikett(einstellungen.Anbieter);
        zeile.AiBaseUrl = einstellungen.Adresse;
        zeile.AiModel = einstellungen.Modell;
        zeile.AiKeyEncrypted = einstellungen.SchluesselVerschluesselt;
        zeile.AiKeyTail = einstellungen.SchluesselEndung;
        zeile.AiAuditLog = einstellungen.KiProtokoll;
        zeile.UpdatedAt = jetzt;
    }

    /// <summary>Das Etikett in der Spalte — snake_case, wie überall auf dem Draht.</summary>
    private static string Etikett(KiAnbieter anbieter) => anbieter switch
    {
        KiAnbieter.OpenAiKompatibel => "openai_compatible",
        KiAnbieter.Anthropic => "anthropic",
        _ => "none"
    };

    /// <summary>
    /// Ein unbekanntes Etikett wird <see cref="KiAnbieter.Keiner"/> — nicht ein Fehler.
    /// </summary>
    /// <remarks>
    /// Das ist die zurückhaltende Richtung: wer nach einem Rückbau eine Zeile
    /// mit einem Anbieter findet, den es nicht mehr gibt, soll niemanden fragen
    /// und nicht abstürzen.
    /// </remarks>
    private static KiAnbieter Anbieter(string etikett) => etikett switch
    {
        "openai_compatible" => KiAnbieter.OpenAiKompatibel,
        "anthropic" => KiAnbieter.Anthropic,
        _ => KiAnbieter.Keiner
    };
}
