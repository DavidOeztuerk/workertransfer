using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Identity.Infrastructure.Nachweis;

/// <summary>Welche KI-Anbieter die Menschen dieser Instanz eingetragen haben.</summary>
/// <remarks>
/// <para><strong>Das ist die Zeile, die kein Test schreiben kann.</strong>
/// <c>Adr0022Tests</c> und <c>EntwurfsgrenzeTests</c> prüfen den Baum; der
/// KI-Zugang steht in <c>KiZugangV1</c> — Anbieter, Adresse, Modell und
/// Schlüssel je Person, zur Laufzeit aus den AccountSettings. Wohin dieser
/// Baum heute Abend spricht, steht in dieser Tabelle und sonst nirgends.</para>
///
/// <para><strong>Gezählt, nie genannt.</strong> Die Abfrage gruppiert nach
/// Anbieter und Adresse und gibt eine Anzahl zurück; <c>subject_id</c> verlässt
/// sie nicht, und die Adresse verliert alles bis auf den Host. Wer welchen
/// Anbieter benutzt, ist eine Aussage über einen Menschen und gehört nicht in
/// ein Dokument, das jemand herumreicht — ADR-0026 sagt dasselbe für
/// Ereignisse.</para>
///
/// <para><strong>Und der Schlüssel wird nicht einmal gelesen.</strong> Die
/// Abfrage wählt drei Spalten aus; <c>ai_key_encrypted</c> ist keine davon. Was
/// nicht geholt wird, kann auch nicht versehentlich in einen Befund geraten.</para>
///
/// <para><c>none</c> fällt heraus: „kein Anbieter" ist kein Empfänger, und eine
/// Zeile darüber wäre ein Eintrag über einen Empfänger, den es nicht gibt.</para>
/// </remarks>
/// <param name="anbieter">
/// Der Container. Der Kontext wird in einem eigenen Bereich aufgelöst: Noelias
/// Prüfungen sind Singletons, der <c>DbContext</c> ist bereichsgebunden — ihn
/// direkt zu nehmen wäre eine gefangene Abhängigkeit, und der Container bricht
/// dann beim Start ab.
/// </param>
public sealed class EfProviderSource(IServiceProvider provider) : IProviderSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderRow>> ReadAsync(CancellationToken ct = default)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var groups = await context.AccountSettings
            .Where(row => row.AiProvider != "none" && row.AiBaseUrl != "")
            .GroupBy(row => new { row.AiProvider, row.AiBaseUrl })
            .Select(group => new
            {
                group.Key.AiProvider,
                group.Key.AiBaseUrl,
                Menschen = group.Count()
            })
            .ToListAsync(ct);

        return
        [
            .. groups
                .Select(group => new
                {
                    group.AiProvider,
                    group.Menschen,
                    Host = Uri.TryCreate(group.AiBaseUrl, UriKind.Absolute, out var target)
                        ? target.Host
                        : string.Empty
                })
                .Where(group => group.Host.Length > 0)
                .Select(group => new ProviderRow(
                    group.AiProvider, group.Host, ProviderOrigin.Person, group.Menschen))
        ];
    }
}
