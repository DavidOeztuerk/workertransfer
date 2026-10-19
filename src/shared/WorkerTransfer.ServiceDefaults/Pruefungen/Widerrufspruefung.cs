using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Pruefungen;

/// <summary>Steht zwischen der Frage an den Ledger und dem Ledger noch etwas?</summary>
/// <remarks>
/// <para><strong>Sie überlebt den Umstieg, weil Noelia den Ledger nicht
/// kennt.</strong> <c>noelia.revocation.*</c> misst Noelias eigene
/// Widerrufsliste für Token; der Einwilligungsledger dieses Produkts ist ein
/// Dienst mit einer eigenen Zusage (ADR-0013): ein Widerruf muss beim NÄCHSTEN
/// Lesen wirken.</para>
///
/// <para><strong>Was gemessen wird, und was nicht.</strong> Gemessen wird,
/// welcher Typ aufgelöst wird, wenn dieser Dienst das Einwilligungstor benutzt
/// — und ob das derselbe ist, der unmittelbar mit dem Ledger spricht. Ein
/// Zwischenspeicher kommt in diesem System nicht als Konfiguration, sondern als
/// ein weiterer Typ dazwischen. Genau den sieht sie.</para>
///
/// <para>Sie behauptet <em>nicht</em>, einen Widerruf ausprobiert zu haben. Das
/// könnte sie nur, indem sie in den Ledger schriebe, und eine Betriebsauskunft,
/// die Einwilligungen gewährt und widerruft, um sich selbst zu belegen, wäre
/// der schlechteste Tausch in diesem Baum.</para>
/// </remarks>
/// <typeparam name="TTor">Der Port, über den dieser Dienst den Ledger fragt.</typeparam>
/// <param name="anbieter">Der Container — es wird in einem eigenen Bereich aufgelöst.</param>
/// <param name="unmittelbar">Der Adapter, der wirklich mit dem Ledger spricht.</param>
public sealed class Widerrufspruefung<TTor>(IServiceProvider anbieter, Type unmittelbar)
    : ISecurityCheck
    where TTor : notnull
{
    /// <inheritdoc />
    public string Id => "wt.einwilligung.wirkt";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Auswahlregel — siehe <c>Anbieterpruefung</c>.</remarks>
    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    /// <inheritdoc />
    public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;

    /// <inheritdoc />
    public string Remediation =>
        "ADR-0013: ein Widerruf muss beim nächsten Lesen wirken. Solange ein "
        + "anderer Typ dazwischensteht, ist ungeklärt, ob er die Antwort "
        + "aufhebt — und ein Cache an dieser Stelle ist kein Leistungsdetail, "
        + "sondern ein Regelbruch, den man nicht bemerkt.";

    /// <inheritdoc />
    public IReadOnlyList<RegulatoryReference> References =>
    [
        Rechtsbezuege.Widerruf,
        Rechtsbezuege.Rechenschaft
    ];

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anbieter);
        ArgumentNullException.ThrowIfNull(unmittelbar);

        // Ein eigener Bereich, weil das Tor je Anfrage lebt — und weil diese
        // Pruefung als Singleton registriert ist. Aus dem Wurzelanbieter heraus
        // aufzuloesen wuerfe bei einem bereichsgebundenen Dienst, und das saehe
        // aus wie ein Befund, waere aber ein Fehler dieser Pruefung.
        using var bereich = anbieter.CreateScope();

        var aufgeloest = bereich.ServiceProvider.GetService<TTor>();

        if (aufgeloest is null)
        {
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.NotApplicable,
                $"Dieser Dienst fragt den Ledger nicht: für {Kurz(typeof(TTor))} "
                + "ist nichts registriert.",
                "Wer hier eine Sichtbarkeit entscheidet, fragt den Ledger — und "
                + "zwar synchron und ohne Zwischenspeicher (ADR-0013)."));
        }

        var tatsaechlich = aufgeloest.GetType();

        return tatsaechlich == unmittelbar
            ? Task.FromResult(Ergebnis(
                SecurityCheckStatus.Pass,
                $"Zwischen der Frage ({Kurz(typeof(TTor))}) und dem Ledger steht "
                + $"kein weiterer Typ: aufgelöst wird {Kurz(unmittelbar)}, der "
                + "Adapter, der unmittelbar fragt. Kein Zwischenspeicher, kein "
                + "Umhüller.",
                "Ein Umhüller an dieser Stelle — auch einer, der nur für eine "
                + "Minute merkt — stößt diesen Befund um. Ein Widerruf wirkte "
                + "dann beim übernächsten Lesen, und niemand sähe es."))
            : Task.FromResult(Ergebnis(
                SecurityCheckStatus.Fail,
                $"Zwischen der Frage ({Kurz(typeof(TTor))}) und dem Ledger steht "
                + $"{Kurz(tatsaechlich)} statt {Kurz(unmittelbar)}.",
                Remediation));
    }

    /// <summary>Ein Typname ohne Namensraum — eine Gestalt, kein Wert.</summary>
    private static string Kurz(Type typ) => typ.Name;

    private SecurityCheckResult Ergebnis(
        SecurityCheckStatus stand, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, stand, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
