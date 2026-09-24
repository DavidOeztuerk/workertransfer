using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>What a service is in the erasure cascade.</summary>
/// <remarks>
/// Three roles and not two. <see cref="Recipient"/> and
/// <see cref="NotInvolved"/> suggest themselves; <see cref="Origin"/> is the
/// one a two-valued question would have answered wrongly: identity-service does
/// not stand in <c>ErasureRecipient.Fremde</c> — it <em>is</em> the cascade —,
/// and "not on the list" would there wrongly mean "holds nothing about a human
/// being".
/// </remarks>
public enum ErasureRole
{
    /// <summary>It sets the cascade going and falls last itself.</summary>
    Origin,

    /// <summary>It holds rows about people and acknowledges.</summary>
    Recipient,

    /// <summary>It holds nothing about a natural person.</summary>
    NotInvolved
}

/// <summary>Can the erasure cascade work, and what remains afterwards as evidence?</summary>
/// <remarks>
/// <para><strong>What is measured here, no test sees.</strong>
/// <c>ErasureRecipientTests</c> checks the <em>list</em> against the EF models,
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
    private readonly string _serviceName;
    private readonly ErasureRole _role;
    private readonly bool _doorConfigured;
    private readonly bool _auditTrailPresent;
    private readonly IReadOnlyList<string> _expected;
    private readonly IReadOnlyList<string> _known;

    private ErasureCheck(
        string serviceName,
        ErasureRole role,
        bool doorConfigured,
        bool auditTrailPresent,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> known)
    {
        _serviceName = serviceName;
        _role = role;
        _doorConfigured = doorConfigured;
        _auditTrailPresent = auditTrailPresent;
        _expected = expected;
        _known = known;
    }

    /// <summary>A service an erasure order has to reach.</summary>
    /// <param name="serviceName">How it is called in the recipient list.</param>
    /// <param name="doorConfigured">Whether the erasure secret is set in this instance.</param>
    /// <param name="auditTrailPresent">Whether an audit trail writes in the same transaction.</param>
    /// <returns>The check.</returns>
    public static ErasureCheck AsRecipient(
        string serviceName, bool doorConfigured, bool auditTrailPresent) =>
        new(serviceName, ErasureRole.Recipient, doorConfigured, auditTrailPresent, [], []);

    /// <summary>The service that sets the cascade going.</summary>
    /// <remarks>
    /// <strong>The question is a different one here</strong>, and it is the
    /// sharpest in the whole area: does the origin know an address for
    /// <em>every</em> recipient? A recipient without an address gets no order;
    /// the cascade then waits for an acknowledgement nobody asked for — and
    /// that is indistinguishable from "the recipient is not answering right
    /// now".
    /// </remarks>
    /// <param name="serviceName">What it calls itself.</param>
    /// <param name="expected">The recipient list (<c>ErasureRecipient.Fremde</c>).</param>
    /// <param name="known">What an address is configured for in this instance.</param>
    /// <returns>The check.</returns>
    public static ErasureCheck AsOrigin(
        string serviceName,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> known) =>
        new(serviceName, ErasureRole.Origin, true, false, expected, known);

    /// <summary>A service without a single row about a natural person.</summary>
    /// <param name="serviceName">What it calls itself.</param>
    /// <returns>The check.</returns>
    public static ErasureCheck WithoutSubjectRows(string serviceName) =>
        new(serviceName, ErasureRole.NotInvolved, false, false, [], []);

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
        "Erasure__Secret setzen, beziehungsweise Erasure__BaseUrls__<dienst> "
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
        Task.FromResult(_role switch
        {
            ErasureRole.NotInvolved => NotInvolved(),
            ErasureRole.Origin => Origin(),
            _ => Recipient()
        });

    private SecurityCheckResult NotInvolved() =>
        Finding(
            SecurityCheckStatus.NotApplicable,
            $"„{_serviceName}“ hält nichts über eine natürliche Person und steht "
            + "deshalb nicht in der Kaskade. Ein Löschauftrag an einen Dienst "
            + "ohne Zeilen wäre ein Endpunkt, der „erledigt“ sagt, ohne je etwas "
            + "getan zu haben.",
            "Sobald hier eine Tabelle entsteht, deren Zeile einem Menschen "
            + "gehört, gehört der Dienst in ErasureRecipient.Services — "
            + "ErasureRecipientTests liest dafür das EF-Modell und nicht den "
            + "Quelltext.");

    private SecurityCheckResult Origin()
    {
        var missing = _expected
            .Where(service => !_known.Contains(service, StringComparer.OrdinalIgnoreCase))
            .OrderBy(service => service, StringComparer.Ordinal)
            .ToList();

        if (missing.Count > 0)
        {
            return Finding(
                SecurityCheckStatus.Fail,
                $"„{_serviceName}“ stößt die Löschkaskade an, kennt aber für "
                + $"{(missing.Count)} von "
                + $"{(_expected.Count)} Empfängern keine Adresse: "
                + $"{string.Join(", ", missing)}.",
                "Erasure__BaseUrls__<dienst> setzen. Ohne Adresse bekommt der "
                + "Empfänger keinen Auftrag, die Kaskade wartet auf eine "
                + "Quittung, die niemand angefordert hat — und von außen sieht "
                + "das aus wie „läuft noch“.");
        }

        return Finding(
            SecurityCheckStatus.Pass,
            $"„{_serviceName}“ stößt die Löschkaskade an und kennt für alle "
            + $"{(_expected.Count)} Empfänger eine Adresse "
            + $"({string.Join(", ", _expected)}). Die eigenen Zeilen fallen "
            + "zuletzt, nach der Quittung jedes Empfängers.",
            "Ein zwölfter Dienst mit Zeilen über Menschen muss in derselben "
            + "Bewegung in ErasureRecipient.Services UND in die Umgebung — sonst "
            + "geht diese Prüfung rot, und das ist ihr Zweck.");
    }

    private SecurityCheckResult Recipient()
    {
        if (!_doorConfigured)
        {
            return Finding(
                SecurityCheckStatus.Fail,
                $"„{_serviceName}“ steht in der Löschkaskade, aber seine Tür ist "
                + "zu: das Löschgeheimnis ist in dieser Instanz nicht gesetzt. "
                + "Ein Löschauftrag erreicht diesen Dienst damit nicht.",
                "Erasure__Secret setzen — dasselbe, das der Ursprung "
                + "vorzeigt, und ausdrücklich ein anderes als das der "
                + "Benachrichtigung. Ohne es bleibt jede Löschung offen: die "
                + "Kaskade wartet auf eine Quittung, die nie kommt. Dass sie "
                + "wartet, ist richtig — sie darf nicht „fertig“ sagen, solange "
                + "ein Empfänger schweigt.");
        }

        if (!_auditTrailPresent)
        {
            return Finding(
                SecurityCheckStatus.Warning,
                $"„{_serviceName}“ ist über die Kaskade erreichbar, hält aber "
                + "keine eigene Prüfspur: was er gelöscht hat, kann er "
                + "hinterher nur behaupten.",
                "ADR-0012: eine Prüfspur ist eine Tabelle in derselben "
                + "Transaktion wie die Änderung, kein Protokolleintrag. Art. 5 "
                + "Abs. 2 DSGVO fragt nach dem Nachweis, nicht nach der "
                + "Absicht. Der Ledger bleibt daneben als Beleg der Kaskade "
                + "selbst bestehen (ADR-0027).");
        }

        return Finding(
            SecurityCheckStatus.Pass,
            $"„{_serviceName}“ steht in der Löschkaskade, seine Tür ist "
            + "eingerichtet, und er schreibt eine Prüfspur in derselben "
            + "Transaktion wie die Löschung.",
            "Ein leeres Erasure__Secret stößt diesen Befund um — und zwar in "
            + "genau der Umgebung, in der es leer ist, nicht im Baum.");
    }

    private SecurityCheckResult Finding(
        SecurityCheckStatus state, string summary, string remediation) =>
        new(Id, Module, Category, state, Severity, summary, remediation)
        {
            References = References
        };
}
