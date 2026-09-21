using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>What a service is in the erasure cascade.</summary>
/// <remarks>
/// Three roles and not two. <see cref="Empfaenger"/> and
/// <see cref="Unbeteiligt"/> suggest themselves; <see cref="Ursprung"/> is the
/// one a two-valued question would have answered wrongly: identity-service does
/// not stand in <c>ErasureRecipient.Fremde</c> — it <em>is</em> the cascade —,
/// and "not on the list" would there wrongly mean "holds nothing about a human
/// being".
/// </remarks>
public enum ErasureRole
{
    /// <summary>It sets the cascade going and falls last itself.</summary>
    Ursprung,

    /// <summary>It holds rows about people and acknowledges.</summary>
    Empfaenger,

    /// <summary>It holds nothing about a natural person.</summary>
    Unbeteiligt
}

/// <summary>Can the erasure cascade work, and what remains afterwards as evidence?</summary>
/// <remarks>
/// <para><strong>What is measured here, no test sees.</strong>
/// <c>LoeschempfaengerTests</c> checks the <em>list</em> against the EF models,
/// and does it well: no service can silently grow a table about people and be
/// missing from the list. What a test cannot know is whether the doors of
/// <em>this instance</em> are open and whether the origin knows every address.
/// Both stand in the environment, both differ per environment, and both fail
/// silently: an erasure without an acknowledgement stays open, and from the
/// outside open looks like "still running".</para>
///
/// <para><strong>That is why <see cref="SecurityCheckStatus.Fail"/> and not a
/// hint.</strong> The erasure promise is the one in this tree where "shut in
/// doubt" and "redeemed in doubt" come apart: keeping the door shut in doubt is
/// decided correctly — and precisely then the promise is not redeemed.</para>
/// </remarks>
public sealed class ErasureCheck : ISecurityCheck
{
    private readonly string _dienstname;
    private readonly ErasureRole _rolle;
    private readonly bool _tuerEingerichtet;
    private readonly bool _pruefspurVorhanden;
    private readonly IReadOnlyList<string> _erwartet;
    private readonly IReadOnlyList<string> _bekannt;

    private ErasureCheck(
        string dienstname,
        ErasureRole role,
        bool tuerEingerichtet,
        bool pruefspurVorhanden,
        IReadOnlyList<string> erwartet,
        IReadOnlyList<string> bekannt)
    {
        _dienstname = dienstname;
        _rolle = role;
        _tuerEingerichtet = tuerEingerichtet;
        _pruefspurVorhanden = pruefspurVorhanden;
        _erwartet = erwartet;
        _bekannt = bekannt;
    }

    /// <summary>A service an erasure order has to reach.</summary>
    /// <param name="dienstname">How it is called in the recipient list.</param>
    /// <param name="tuerEingerichtet">Whether the erasure secret is set in this instance.</param>
    /// <param name="pruefspurVorhanden">Whether an audit trail writes in the same transaction.</param>
    /// <returns>The check.</returns>
    public static ErasureCheck AlsEmpfaenger(
        string dienstname, bool tuerEingerichtet, bool pruefspurVorhanden) =>
        new(dienstname, ErasureRole.Empfaenger, tuerEingerichtet, pruefspurVorhanden, [], []);

    /// <summary>The service that sets the cascade going.</summary>
    /// <remarks>
    /// <strong>The question is a different one here</strong>, and it is the
    /// sharpest in the whole area: does the origin know an address for
    /// <em>every</em> recipient? A recipient without an address gets no order;
    /// the cascade then waits for an acknowledgement nobody asked for — and
    /// that is indistinguishable from "the recipient is not answering right
    /// now".
    /// </remarks>
    /// <param name="dienstname">What it calls itself.</param>
    /// <param name="erwartet">The recipient list (<c>ErasureRecipient.Fremde</c>).</param>
    /// <param name="bekannt">What an address is configured for in this instance.</param>
    /// <returns>The check.</returns>
    public static ErasureCheck AlsUrsprung(
        string dienstname,
        IReadOnlyList<string> erwartet,
        IReadOnlyList<string> bekannt) =>
        new(dienstname, ErasureRole.Ursprung, true, false, erwartet, bekannt);

    /// <summary>A service without a single row about a natural person.</summary>
    /// <param name="dienstname">What it calls itself.</param>
    /// <returns>The check.</returns>
    public static ErasureCheck OhneZeilen(string dienstname) =>
        new(dienstname, ErasureRole.Unbeteiligt, false, false, [], []);

    /// <inheritdoc />
    public string Id => "wt.loeschung.nachweis";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Selection rule — see <c>ProviderCheck</c>.</remarks>
    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    /// <inheritdoc />
    public SecurityCheckSeverity Severity => SecurityCheckSeverity.High;

    /// <inheritdoc />
    public string Remediation =>
        "Erasure__Geheimnis setzen, beziehungsweise Erasure__Adressen__<dienst> "
        + "beim Ursprung. Ohne sie bleibt jede Loeschung offen: die Kaskade "
        + "wartet auf eine Quittung, die nie kommt.";

    /// <inheritdoc />
    public IReadOnlyList<RegulatoryReference> References =>
    [
        AdditionalRegulatoryReferences.Erasure,
        AdditionalRegulatoryReferences.Accountability
    ];

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_rolle switch
        {
            ErasureRole.Unbeteiligt => Unbeteiligt(),
            ErasureRole.Ursprung => Ursprung(),
            _ => Empfaenger()
        });

    private SecurityCheckResult Unbeteiligt() =>
        Ergebnis(
            SecurityCheckStatus.NotApplicable,
            $"„{_dienstname}“ hält nichts über eine natürliche Person und steht "
            + "deshalb nicht in der Kaskade. Ein Löschauftrag an einen Dienst "
            + "ohne Zeilen wäre ein Endpunkt, der „erledigt“ sagt, ohne je etwas "
            + "getan zu haben.",
            "Sobald hier eine Tabelle entsteht, deren Zeile einem Menschen "
            + "gehört, gehört der Dienst in Loeschempfaenger.Fremde — "
            + "LoeschempfaengerTests liest dafür das EF-Modell und nicht den "
            + "Quelltext.");

    private SecurityCheckResult Ursprung()
    {
        var fehlend = _erwartet
            .Where(service => !_bekannt.Contains(service, StringComparer.OrdinalIgnoreCase))
            .OrderBy(service => service, StringComparer.Ordinal)
            .ToList();

        if (fehlend.Count > 0)
        {
            return Ergebnis(
                SecurityCheckStatus.Fail,
                $"„{_dienstname}“ stößt die Löschkaskade an, kennt aber für "
                + $"{(fehlend.Count)} von "
                + $"{(_erwartet.Count)} Empfängern keine Adresse: "
                + $"{string.Join(", ", fehlend)}.",
                "Erasure__Adressen__<dienst> setzen. Ohne Adresse bekommt der "
                + "Empfänger keinen Auftrag, die Kaskade wartet auf eine "
                + "Quittung, die niemand angefordert hat — und von außen sieht "
                + "das aus wie „läuft noch“.");
        }

        return Ergebnis(
            SecurityCheckStatus.Pass,
            $"„{_dienstname}“ stößt die Löschkaskade an und kennt für alle "
            + $"{(_erwartet.Count)} Empfänger eine Adresse "
            + $"({string.Join(", ", _erwartet)}). Die eigenen Zeilen fallen "
            + "zuletzt, nach der Quittung jedes Empfängers.",
            "Ein zwölfter Dienst mit Zeilen über Menschen muss in derselben "
            + "Bewegung in Loeschempfaenger.Fremde UND in die Umgebung — sonst "
            + "geht diese Prüfung rot, und das ist ihr Zweck.");
    }

    private SecurityCheckResult Empfaenger()
    {
        if (!_tuerEingerichtet)
        {
            return Ergebnis(
                SecurityCheckStatus.Fail,
                $"„{_dienstname}“ steht in der Löschkaskade, aber seine Tür ist "
                + "zu: das Löschgeheimnis ist in dieser Instanz nicht gesetzt. "
                + "Ein Löschauftrag erreicht diesen Dienst damit nicht.",
                "Erasure__Geheimnis setzen — dasselbe, das der Ursprung "
                + "vorzeigt, und ausdrücklich ein anderes als das der "
                + "Benachrichtigung. Ohne es bleibt jede Löschung offen: die "
                + "Kaskade wartet auf eine Quittung, die nie kommt. Dass sie "
                + "wartet, ist richtig — sie darf nicht „fertig“ sagen, solange "
                + "ein Empfänger schweigt.");
        }

        if (!_pruefspurVorhanden)
        {
            return Ergebnis(
                SecurityCheckStatus.Warning,
                $"„{_dienstname}“ ist über die Kaskade erreichbar, hält aber "
                + "keine eigene Prüfspur: was er gelöscht hat, kann er "
                + "hinterher nur behaupten.",
                "ADR-0012: eine Prüfspur ist eine Tabelle in derselben "
                + "Transaktion wie die Änderung, kein Protokolleintrag. Art. 5 "
                + "Abs. 2 DSGVO fragt nach dem Nachweis, nicht nach der "
                + "Absicht. Der Ledger bleibt daneben als Beleg der Kaskade "
                + "selbst bestehen (ADR-0027).");
        }

        return Ergebnis(
            SecurityCheckStatus.Pass,
            $"„{_dienstname}“ steht in der Löschkaskade, seine Tür ist "
            + "eingerichtet, und er schreibt eine Prüfspur in derselben "
            + "Transaktion wie die Löschung.",
            "Ein leeres Erasure__Geheimnis stößt diesen Befund um — und zwar in "
            + "genau der Umgebung, in der es leer ist, nicht im Baum.");
    }

    private SecurityCheckResult Ergebnis(
        SecurityCheckStatus state, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, state, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
