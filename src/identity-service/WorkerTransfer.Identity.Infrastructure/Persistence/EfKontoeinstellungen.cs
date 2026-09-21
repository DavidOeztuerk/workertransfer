using Noelia.Core.Identity;
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
public sealed class EfAccountSettingsStore(IdentityDbContext context, TimeProvider uhr)
    : IAccountSettingsStore
{
    /// <inheritdoc />
    public async Task<AccountSettings> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.AccountSettings
            .FirstOrDefaultAsync(entry => entry.SubjectId == subject.Value, cancellationToken);

        return row is null
            ? AccountSettings.Vorgabe(subject)
            : AccountSettings.Wiederherstellen(
                subject,
                row.DeleteAfterMonths,
                Anbieter(row.AiProvider),
                row.AiBaseUrl,
                row.AiModel,
                row.AiKeyEncrypted,
                row.AiKeyTail,
                row.AiAuditLog);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        AccountSettings einstellungen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(einstellungen);

        var now = uhr.GetUtcNow().UtcDateTime;

        var row = await context.AccountSettings
            .AsTracking()
            .FirstOrDefaultAsync(
                entry => entry.SubjectId == einstellungen.Wer.Value, cancellationToken);

        if (row is null)
        {
            row = new AccountSettingsRow
            {
                SubjectId = einstellungen.Wer.Value,
                CreatedAt = now
            };
            context.AccountSettings.Add(row);
        }

        row.DeleteAfterMonths = einstellungen.LoeschungNachMonaten;
        row.AiProvider = Etikett(einstellungen.Anbieter);
        row.AiBaseUrl = einstellungen.Adresse;
        row.AiModel = einstellungen.Modell;
        row.AiKeyEncrypted = einstellungen.SchluesselVerschluesselt;
        row.AiKeyTail = einstellungen.SchluesselEndung;
        row.AiAuditLog = einstellungen.KiProtokoll;
        row.UpdatedAt = now;
    }

    /// <summary>Das Etikett in der Spalte — snake_case, wie überall auf dem Draht.</summary>
    private static string Etikett(AiProvider provider) => provider switch
    {
        AiProvider.OpenAiKompatibel => "openai_compatible",
        AiProvider.Anthropic => "anthropic",
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
    private static AiProvider Anbieter(string label) => label switch
    {
        "openai_compatible" => AiProvider.OpenAiKompatibel,
        "anthropic" => AiProvider.Anthropic,
        _ => AiProvider.None
    };
}
