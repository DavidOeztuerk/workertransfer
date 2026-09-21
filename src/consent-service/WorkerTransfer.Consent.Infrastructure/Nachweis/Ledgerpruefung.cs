using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.Consent.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Consent.Infrastructure.Nachweis;

/// <summary>Der Ledger hält eine Einwilligung der Person, nicht des Mandanten.</summary>
/// <remarks>
/// <para><strong>Die Tatsache, die hier gemessen wird, ist ein Nicht-Vorhandensein</strong>
/// — und die sind die, die am leichtesten wieder verschwinden. Eine Einwilligung
/// gehört dem Menschen und folgt ihm über Arbeitgeber hinweg (ADR-0017); eine
/// Mandantenspalte an dieser Tabelle würde sie an ein Unternehmen binden, und
/// beim Wechsel wäre sie weg. Genau deshalb steht in CLAUDE.md der Satz „Do not
/// ‚fix‘ that by adding one“ — dieser Befund ist derselbe Satz, nur ausführbar
/// und datiert.</para>
///
/// <para><strong>Gelesen wird das EF-Modell, nicht der Quelltext.</strong>
/// Dieselbe Bewegung wie in <c>LoeschempfaengerTests</c> und
/// <c>AuflagenTests</c>: eine Spalte, die über eine Konvention oder eine
/// Basisklasse hereinkäme, stünde in keiner Datei, die ein Textsucher
/// durchsieht. Das Modell wird dafür gebaut, nicht abgefragt — es braucht keine
/// Connection, und diese Prüfung rührt die Datenbank nicht an.</para>
///
/// <para>Der Ledger kennt außerdem <strong>keine Verweigerung</strong>: jede
/// Fähigkeit ist eine Sichtbarkeit, und es gibt nur Gewähren und Widerrufen
/// (ADR-0037). „Sichtbar für alle außer X“ lässt sich darin nicht ausdrücken —
/// weshalb der Modus „alle“ aufhört zu existieren, sobald jemand ein X nennt.
/// Auch das ist eine Handlung, die es nicht gibt, und deshalb wird sie hier
/// gezählt statt beschrieben.</para>
/// </remarks>
/// <param name="anbieter">
/// Der Container. Der Kontext wird in einem EIGENEN BEREICH aufgeloest, und das
/// ist kein Umstand, sondern Pflicht: Noelias Pruefungen sind Singletons, der
/// <c>DbContext</c> ist bereichsgebunden. Ihn in ein Singleton zu ziehen waere
/// eine gefangene Abhaengigkeit — ein Kontext, der ewig lebt, und der erste
/// Fehler daran faellt Wochen spaeter an einer ganz anderen Job auf.
/// </param>
public sealed class LedgerCheck(IServiceProvider provider) : ISecurityCheck
{
    /// <summary>Spaltennamen, die einen Mandanten bezeichnen.</summary>
    private static readonly string[] Mandantenworte =
        ["tenant", "mandant", "company", "firma", "unternehmen", "arbeitgeber"];

    /// <inheritdoc />
    public string Id => "wt.ledger.person";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Auswahlregel — siehe <c>ProviderCheck</c>.</remarks>
    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    /// <inheritdoc />
    public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;

    /// <inheritdoc />
    public string Remediation =>
        "Eine Einwilligung gehoert der Person und folgt ihr ueber Arbeitgeber "
        + "hinweg (ADR-0017). Eine Mandantenspalte bindet sie an ein "
        + "Unternehmen — beim Wechsel waere sie weg, und niemand haette sie "
        + "widerrufen.";

    /// <inheritdoc />
    public IReadOnlyList<RegulatoryReference> References =>
    [
        AdditionalRegulatoryReferences.Revocation,
        AdditionalRegulatoryReferences.Accountability
    ];

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ConsentDbContext>();

        // NUR DER LEDGER, und das ist beim ersten Lauf gegen den echten Stapel
        // gemessen worden: ueber ALLE Entitaeten gelesen meldete diese Pruefung
        // `audit_events.TenantId` — und lag falsch. Die Pruefspur haelt fest,
        // WER gehandelt hat und in welcher Eigenschaft (ADR-0012); dass dort
        // ein Mandant steht, wenn jemand fuer ein Unternehmen handelte, ist die
        // Aussage dieser Tabelle und nicht ihr Fehler. Der Ledger dagegen haelt
        // die Einwilligung selbst, und die gehoert der Person.
        //
        // Zwei Tabellen, zwei Regeln — eine Pruefung, die beide ueber einen
        // Kamm schert, ist nicht strenger, sondern unbrauchbar: sie meldet rot
        // ueber korrekten Code, und beim zweiten Mal schaltet sie jemand ab.
        var ledger = context.Model.FindEntityType(typeof(ConsentEventRow));

        var columns = ledger is null
            ? []
            : ledger.GetProperties()
                .Select(eigenschaft => (
                    Tabelle: ledger.GetTableName() ?? ledger.ShortName(),
                    Spalte: eigenschaft.Name))
                .ToList();

        if (columns.Count == 0)
        {
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.Fail,
                "Das Datenmodell des Ledgers ist leer — diese Prüfung hat nichts "
                + "angesehen und belegt damit nichts.",
                "Nachsehen, ob der Kontext die Ledger-Zeile noch kennt: eine "
                + "umbenannte Entität macht diese Prüfung stumm, und stumm "
                + "sieht aus wie in Ordnung."));
        }

        var hit = columns
            .Where(entry => Mandantenworte.Any(word =>
                entry.Spalte.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => $"{entry.Tabelle}.{entry.Spalte}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (hit.Count > 0)
        {
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.Fail,
                $"Der Ledger trägt {hit.Count} Spalte(n), die einen "
                + $"Mandanten bezeichnen: {string.Join(", ", hit)}.",
                "Eine Einwilligung gehört der Person und folgt ihr über "
                + "Arbeitgeber hinweg (ADR-0017). Eine Mandantenspalte bindet "
                + "sie an ein Unternehmen — beim Wechsel wäre sie weg, und "
                + "niemand hätte es widerrufen."));
        }

        return Task.FromResult(Ergebnis(
            SecurityCheckStatus.Pass,
            $"Keine der {columns.Count} Spalten der Ledger-Tabelle bezeichnet einen "
            + "Mandanten: eine Einwilligung gehört der Person und folgt ihr über "
            + "Arbeitgeber hinweg. Der Ledger kennt außerdem nur Gewähren und "
            + "Widerrufen — eine Verweigerung gibt es nicht, und „sichtbar für "
            + "alle außer X“ lässt sich darin nicht ausdrücken.",
            "Eine Spalte mit „tenant“, „company“ oder „arbeitgeber“ an einer "
            + "dieser Tabellen stößt diesen Befund um — und wäre das Ende der "
            + "Zusage, dass eine Einwilligung einen Arbeitgeberwechsel "
            + "überlebt."));
    }

    private SecurityCheckResult Ergebnis(
        SecurityCheckStatus state, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, state, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
