using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Pruefungen;

namespace WorkerTransfer.Identity.Infrastructure.Nachweis;

/// <summary>Welche KI-Anbieter die Menschen dieser Instanz eingetragen haben.</summary>
/// <remarks>
/// <para><strong>Das ist die Zeile, die kein Test schreiben kann.</strong>
/// <c>Adr0022Tests</c> und <c>EntwurfsgrenzeTests</c> prüfen den Baum; der
/// KI-Zugang steht in <c>KiZugangV1</c> — Anbieter, Adresse, Modell und
/// Schlüssel je Person, zur Laufzeit aus den Kontoeinstellungen. Wohin dieser
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
public sealed class EfAnbieterquelle(IServiceProvider anbieter) : IAnbieterquelle
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Anbieterzeile>> LeseAsync(CancellationToken ct = default)
    {
        using var bereich = anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var gruppen = await kontext.AccountSettings
            .Where(zeile => zeile.AiProvider != "none" && zeile.AiBaseUrl != "")
            .GroupBy(zeile => new { zeile.AiProvider, zeile.AiBaseUrl })
            .Select(gruppe => new
            {
                gruppe.Key.AiProvider,
                gruppe.Key.AiBaseUrl,
                Menschen = gruppe.Count()
            })
            .ToListAsync(ct);

        return
        [
            .. gruppen
                .Select(gruppe => new
                {
                    gruppe.AiProvider,
                    gruppe.Menschen,
                    Host = Uri.TryCreate(gruppe.AiBaseUrl, UriKind.Absolute, out var ziel)
                        ? ziel.Host
                        : string.Empty
                })
                .Where(gruppe => gruppe.Host.Length > 0)
                .Select(gruppe => new Anbieterzeile(
                    gruppe.AiProvider, gruppe.Host, Herkunft.Person, gruppe.Menschen))
        ];
    }
}
