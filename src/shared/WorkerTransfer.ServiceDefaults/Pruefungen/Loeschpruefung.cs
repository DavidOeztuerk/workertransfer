using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Pruefungen;

/// <summary>Was ein Dienst in der Löschkaskade ist.</summary>
/// <remarks>
/// Drei Rollen und nicht zwei. <see cref="Empfaenger"/> und
/// <see cref="Unbeteiligt"/> liegen nahe; <see cref="Ursprung"/> ist der, den
/// eine zweiwertige Frage falsch beantwortet hätte: identity-service steht
/// nicht in <c>Loeschempfaenger.Fremde</c> — es <em>ist</em> die Kaskade —, und
/// „nicht auf der Liste“ hieße dort fälschlich „hält nichts über einen
/// Menschen“.
/// </remarks>
public enum Loeschrolle
{
    /// <summary>Er stößt die Kaskade an und fällt zuletzt selbst.</summary>
    Ursprung,

    /// <summary>Er hält Zeilen über Menschen und quittiert.</summary>
    Empfaenger,

    /// <summary>Er hält nichts über eine natürliche Person.</summary>
    Unbeteiligt
}

/// <summary>Kann die Löschkaskade wirken, und was bleibt danach als Beleg?</summary>
/// <remarks>
/// <para><strong>Was hier gemessen wird, sieht kein Test.</strong>
/// <c>LoeschempfaengerTests</c> prüft die <em>Liste</em> gegen die EF-Modelle,
/// und zwar gut: kein Dienst kann still eine Personentabelle bekommen und von
/// der Liste fehlen. Was ein Test nicht wissen kann, ist, ob die Türen
/// <em>dieser Instanz</em> offen sind und ob der Ursprung jede Adresse kennt.
/// Beides steht in der Umgebung, beides ist je Umgebung anders, und beides
/// scheitert lautlos: eine Löschung ohne Quittung bleibt offen, und offen sieht
/// von außen aus wie „läuft noch“.</para>
///
/// <para><strong>Deshalb <see cref="Stand.Fehlt"/> und kein Hinweis.</strong>
/// Die Löschzusage ist die eine in diesem Baum, bei der „im Zweifel zu“ und „im
/// Zweifel eingelöst“ auseinanderfallen: die Tür im Zweifel zu zu halten ist
/// richtig entschieden — und genau dann ist die Zusage nicht eingelöst.</para>
/// </remarks>
public sealed class Loeschpruefung : ISecurityCheck
{
    private readonly string _dienstname;
    private readonly Loeschrolle _rolle;
    private readonly bool _tuerEingerichtet;
    private readonly bool _pruefspurVorhanden;
    private readonly IReadOnlyList<string> _erwartet;
    private readonly IReadOnlyList<string> _bekannt;

    private Loeschpruefung(
        string dienstname,
        Loeschrolle rolle,
        bool tuerEingerichtet,
        bool pruefspurVorhanden,
        IReadOnlyList<string> erwartet,
        IReadOnlyList<string> bekannt)
    {
        _dienstname = dienstname;
        _rolle = rolle;
        _tuerEingerichtet = tuerEingerichtet;
        _pruefspurVorhanden = pruefspurVorhanden;
        _erwartet = erwartet;
        _bekannt = bekannt;
    }

    /// <summary>Ein Dienst, den ein Löschauftrag erreichen muss.</summary>
    /// <param name="dienstname">Wie er in der Empfängerliste heißt.</param>
    /// <param name="tuerEingerichtet">Ob das Löschgeheimnis in dieser Instanz gesetzt ist.</param>
    /// <param name="pruefspurVorhanden">Ob eine Prüfspur in derselben Transaktion schreibt.</param>
    /// <returns>Die Prüfung.</returns>
    public static Loeschpruefung AlsEmpfaenger(
        string dienstname, bool tuerEingerichtet, bool pruefspurVorhanden) =>
        new(dienstname, Loeschrolle.Empfaenger, tuerEingerichtet, pruefspurVorhanden, [], []);

    /// <summary>Der Dienst, der die Kaskade anstößt.</summary>
    /// <remarks>
    /// <strong>Die Frage ist hier eine andere</strong>, und sie ist die
    /// schärfste im ganzen Bereich: kennt der Ursprung für <em>jeden</em>
    /// Empfänger eine Adresse? Ein Empfänger ohne Adresse bekommt keinen
    /// Auftrag; die Kaskade wartet dann auf eine Quittung, die niemand
    /// angefordert hat — und das ist von „der Empfänger antwortet gerade nicht“
    /// nicht zu unterscheiden.
    /// </remarks>
    /// <param name="dienstname">Wie er sich nennt.</param>
    /// <param name="erwartet">Die Empfängerliste (<c>Loeschempfaenger.Fremde</c>).</param>
    /// <param name="bekannt">Wofür in dieser Instanz eine Adresse konfiguriert ist.</param>
    /// <returns>Die Prüfung.</returns>
    public static Loeschpruefung AlsUrsprung(
        string dienstname,
        IReadOnlyList<string> erwartet,
        IReadOnlyList<string> bekannt) =>
        new(dienstname, Loeschrolle.Ursprung, true, false, erwartet, bekannt);

    /// <summary>Ein Dienst ohne eine einzige Zeile über eine natürliche Person.</summary>
    /// <param name="dienstname">Wie er sich nennt.</param>
    /// <returns>Die Prüfung.</returns>
    public static Loeschpruefung OhneZeilen(string dienstname) =>
        new(dienstname, Loeschrolle.Unbeteiligt, false, false, [], []);

    /// <inheritdoc />
    public string Id => "wt.loeschung.nachweis";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Auswahlregel — siehe <c>Anbieterpruefung</c>.</remarks>
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
        Rechtsbezuege.Loeschung,
        Rechtsbezuege.Rechenschaft
    ];

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_rolle switch
        {
            Loeschrolle.Unbeteiligt => Unbeteiligt(),
            Loeschrolle.Ursprung => Ursprung(),
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
            .Where(dienst => !_bekannt.Contains(dienst, StringComparer.OrdinalIgnoreCase))
            .OrderBy(dienst => dienst, StringComparer.Ordinal)
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
        SecurityCheckStatus stand, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, stand, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
