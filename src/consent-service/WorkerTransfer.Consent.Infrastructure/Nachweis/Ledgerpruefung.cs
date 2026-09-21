using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.Consent.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Consent.Infrastructure.Nachweis;

/// <summary>The ledger holds a consent of the person, not of the tenant.</summary>
/// <remarks>
/// <para><strong>The fact measured here is a non-existence</strong> — and those
/// are the ones that disappear again most easily. A consent belongs to the human
/// being and follows them across employers (ADR-0017); a tenant column on this
/// table would bind it to a company, and on a change of employer it would be
/// gone. That is exactly why CLAUDE.md carries the sentence „Do not ‚fix‘ that
/// by adding one“ — this finding is the same sentence, only executable and
/// dated.</para>
///
/// <para><strong>What is read is the EF model, not the source text.</strong> The
/// same movement as in <c>LoeschempfaengerTests</c> and <c>AuflagenTests</c>: a
/// column arriving through a convention or a base class would stand in no file a
/// text search looks through. The model is built for this, not queried — it
/// needs no connection, and this check does not touch the database.</para>
///
/// <para>The ledger moreover knows <strong>no denial</strong>: every capability
/// is a visibility, and there is only granting and revoking (ADR-0037).
/// „Visible to everyone except X“ cannot be expressed in it — which is why the
/// mode „everyone“ stops existing the moment somebody names an X. That too is
/// an action that does not exist, and that is why it is counted here rather than
/// described.</para>
/// </remarks>
/// <param name="provider">
/// The container. The context is resolved in A SCOPE OF ITS OWN, and that is no
/// inconvenience but a duty: Noelia's checks are singletons, the
/// <c>DbContext</c> is scoped. Pulling it into a singleton would be a captive
/// dependency — a context that lives for ever, and the first fault from it
/// surfaces weeks later in a completely different place.
/// </param>
public sealed class LedgerCheck(IServiceProvider provider) : ISecurityCheck
{
    /// <summary>Column names that denote a tenant.</summary>
    private static readonly string[] TenantWords =
        ["tenant", "mandant", "company", "firma", "unternehmen", "arbeitgeber"];

    /// <inheritdoc />
    public string Id => "wt.ledger.person";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Selection rule — see <c>ProviderCheck</c>.</remarks>
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

        // THE LEDGER ONLY, and that was measured on the first run against the
        // real stack: read over ALL entities this check reported
        // `audit_events.TenantId` — and was wrong. The audit trail records WHO
        // acted and in which capacity (ADR-0012); that a tenant stands there
        // when somebody acted for a company is that table's statement and not
        // its fault. The ledger, by contrast, holds the consent itself, and that
        // belongs to the person.
        //
        // Two tables, two rules — a check that tars both with the same brush is
        // not stricter but useless: it reports red over correct code, and the
        // second time somebody switches it off.
        var ledger = context.Model.FindEntityType(typeof(ConsentEventRow));

        var columns = ledger is null
            ? []
            : ledger.GetProperties()
                .Select(property => (
                    Table: ledger.GetTableName() ?? ledger.ShortName(),
                    Column: property.Name))
                .ToList();

        if (columns.Count == 0)
        {
            return Task.FromResult(Result(
                SecurityCheckStatus.Fail,
                "Das Datenmodell des Ledgers ist leer — diese Prüfung hat nichts "
                + "angesehen und belegt damit nichts.",
                "Nachsehen, ob der Kontext die Ledger-Zeile noch kennt: eine "
                + "umbenannte Entität macht diese Prüfung stumm, und stumm "
                + "sieht aus wie in Ordnung."));
        }

        var hit = columns
            .Where(entry => TenantWords.Any(word =>
                entry.Column.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(entry => $"{entry.Table}.{entry.Column}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (hit.Count > 0)
        {
            return Task.FromResult(Result(
                SecurityCheckStatus.Fail,
                $"Der Ledger trägt {hit.Count} Spalte(n), die einen "
                + $"Mandanten bezeichnen: {string.Join(", ", hit)}.",
                "Eine Einwilligung gehört der Person und folgt ihr über "
                + "Arbeitgeber hinweg (ADR-0017). Eine Mandantenspalte bindet "
                + "sie an ein Unternehmen — beim Wechsel wäre sie weg, und "
                + "niemand hätte es widerrufen."));
        }

        return Task.FromResult(Result(
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

    private SecurityCheckResult Result(
        SecurityCheckStatus state, string summary, string remediation) =>
        new(Id, Module, Category, state, Severity, summary, remediation)
        {
            References = References
        };
}
