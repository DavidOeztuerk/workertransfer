using System.Reflection;
using System.Text.RegularExpressions;
using Noelia.Abstractions.Compliance;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace WorkerTransfer.ServiceDefaults.Checks;

/// <summary>A name where a word from the forbidden list is a homonym.</summary>
/// <remarks>
/// <para><strong>Per name, never per service and never per word.</strong> An
/// exemption for a <em>word</em> would take the check out of operation at that
/// place altogether; an exemption for a <em>service</em> would be the place
/// where nobody looks at the next field any more. A name is a single decision,
/// and exactly one is taken here.</para>
///
/// <para><strong>And it stands in the finding.</strong> That is the actual
/// mechanism: whoever adds an exemption writes its reason into a document a
/// works council reads. A silent list in a test project grows; one that gets
/// read does not.</para>
///
/// <para>The three language catalogues of the interface keep the same form for
/// genuine cognates ("Status", "Website", "Administrator"): short, and every
/// line an individual case.</para>
/// </remarks>
/// <param name="Name">The full name as the check forms it: <c>Type.Member</c> or <c>Type</c>.</param>
/// <param name="Grund">Why this word is not a judgement about a human being here.</param>
public sealed record TermException(string Name, string Grund);

/// <summary>Does any public surface carry a number about a human being?</summary>
/// <remarks>
/// <para><strong>The run-time twin of <c>Adr0022Tests</c> and
/// <c>AuflagenTests</c>.</strong> It reads the domain and the contracts of the
/// service that is currently running and asks the question ADR-0022 §1 asks: is
/// there a name here that announces a number about a human being?</para>
///
/// <para><strong>Why this stands in the evidence and not only in the
/// test.</strong> The human being who answers the Annex III question asks first
/// whether this system summarises applicants in a number. "In the domain and
/// the contracts of profile-service no name carries one of the words" is a
/// piece of evidence they can cite, together with a date. A green test in a
/// test project is not one — it stands in no document.</para>
///
/// <para><strong>The comparison is per syllable and at the syllable start —
/// not as a substring.</strong> That is measured and not chosen: a substring
/// search over this tree reported <c>Capability</c> (the ledger's core type)
/// because of <c>ability</c>, <c>Benefits</c> because of <c>fit</c> and
/// <c>Availability</c> because of <c>ability</c> — three false alarms over
/// perfectly correct code. Girder 4.3.0 had exactly this bug in its masking,
/// and 4.4.0 fixed it with the same movement.</para>
///
/// <para><strong>What the syllable start does not find stands here so that
/// nobody believes otherwise:</strong> a German compound carrying the word at
/// the <em>end</em> escapes it — <c>Trefferanzahl</c> is one syllable and does
/// not begin with <c>anzahl</c>. The rule is therefore not complete, and it
/// never was: the name is the signal, not the rule. A field that carries a
/// number about a human being and has an inconspicuous name is found by no word
/// list — that is what the closed field sets are for.</para>
///
/// <para><strong>Type names and properties are checked, nothing else.</strong>
/// Not <c>Equals</c>, not <c>Deconstruct</c>, not the operators: what the
/// compiler hangs onto a record carries the type name and reported it a dozen
/// times. And not the comments — "without an evaluation" is a promise and
/// should be allowed to stand there.</para>
/// </remarks>
/// <param name="flaechen">The domain and the contracts of this service.</param>
/// <param name="exceptions">Names where a word is a genuine homonym.</param>
/// <param name="wortschatz">
/// The words. The default is <see cref="Kern"/>; a service may be stricter —
/// scout-service additionally forbids <c>bewert</c> in its own conditions,
/// where a word would stand beside a name.
/// </param>
public sealed class NumberCheck(
    IReadOnlyList<Assembly> flaechen,
    IReadOnlyList<TermException>? exceptions = null,
    IReadOnlyList<string>? wortschatz = null) : ISecurityCheck
{
    /// <summary>Words that announce a number or a ranking about a human being.</summary>
    /// <remarks>
    /// <para><strong>About numbers, not about judgements — and that is a
    /// measurement.</strong> <c>bewert</c> stood here and has fallen:
    /// assessment-service holds <em>exactly one</em> evaluation per case, as
    /// text, and the person always reads it (ADR-0042). Forbidding the word
    /// platform-wide would mean excusing thirty names one by one that are all
    /// named correctly — and after the third excuse nobody reads the list any
    /// more. scout-service still forbids it locally, and there it belongs: an
    /// evaluation beside a search hit would be the number ADR-0022 §1
    /// means.</para>
    ///
    /// <para><c>ability</c> and <c>talent</c> do not stand here either.
    /// <c>Capability</c> is the ledger's core type, and what a human being can
    /// do is called <c>Faehigkeit</c> in this tree — a <em>naming</em> by the
    /// person and expressly permitted (ADR-0023). A word that produces nothing
    /// but false alarms weakens the whole list.</para>
    ///
    /// <para><c>note</c> stays although it costs seven exemptions: the German
    /// word is the most dangerous vocabulary a placement platform can have, and
    /// ADR-0042 builds the whole service around forbidding it. That in English
    /// it means "remark" is the price — and each of the seven exemptions says
    /// so in one sentence somebody reads.</para>
    /// </remarks>
    public static IReadOnlyList<string> Kern { get; } =
    [
        "score", "punkt", "rank", "rang", "passung", "fit", "percent", "prozent",
        "quote", "anzahl", "gewicht", "weight", "level", "note", "match",
        "probability", "wahrscheinlich", "aktivitaet", "activity"
    ];

    /// <summary>The words this check runs.</summary>
    public IReadOnlyList<string> Verboten { get; } = wortschatz ?? Kern;

    /// <inheritdoc />
    public string Id => "wt.ki.keine-zahl";

    /// <inheritdoc />
    public NoeliaModule Module => NoeliaModule.Composition;

    /// <inheritdoc />
    /// <remarks>Selection rule — see <c>ProviderCheck</c>.</remarks>
    public SecurityCheckCategory Category => SecurityCheckCategory.Composition;

    /// <inheritdoc />
    public SecurityCheckSeverity Severity => SecurityCheckSeverity.Critical;

    /// <inheritdoc />
    /// <remarks>
    /// "No number about a human being" is the fact that comes closest to
    /// Art. 22 GDPR without answering it — and it is the first thing asked for
    /// when somebody classifies the use under Annex III.
    /// <para>
    /// <strong>§ 87 (1) no. 6 BetrVG belongs here and does not stand
    /// here</strong>, but in the text of the remediation:
    /// <c>RegulatoryRegime</c> knows no national labour law
    /// (<c>bugs/betrvg-hat-kein-regelwerk.md</c>). Putting it under <c>Gdpr</c>
    /// would be a wrong citation in a document a works council reads — worse
    /// than none.
    /// </para>
    /// </remarks>
    public IReadOnlyList<RegulatoryReference> References =>
    [
        AdditionalRegulatoryReferences.AutomatedDecision,
        AdditionalRegulatoryReferences.AnnexThree
    ];

    /// <inheritdoc />
    public string Remediation =>
        "ADR-0022 §1: eine Zahl, die einen Menschen zusammenfasst, samt jeder "
        + "Rangfolge daraus, gibt es hier nicht. Entweder das Feld faellt, oder "
        + "es benennt eine Sache statt einer Person. Ein echtes Homonym wird "
        + "einzeln begruendet und steht dann in diesem Befund. — "
        + AdditionalRegulatoryReferences.WorksCouncilCodetermination;

    /// <inheritdoc />
    public Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flaechen);

        var namen = Namen();

        if (namen.Count == 0)
        {
            // Otherwise the check would report green over nothing — the same
            // defect `scripts/test-dotnet.sh` closes with its count on screen.
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.Fail,
                "Die abgesuchten Flächen sind leer — diese Prüfung hat nichts "
                + "angesehen und belegt damit nichts.",
                "Im Verbundpunkt des Dienstes nachsehen, welche Assemblies "
                + "übergeben werden."));
        }

        var erlaubt = exceptions ?? [];

        // An exemption for a name that no longer exists is a licence nobody
        // needs any more and nobody withdraws.
        var verwaist = erlaubt
            .Where(exception => !namen.Contains(exception.Name))
            .Select(exception => exception.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (verwaist.Count > 0)
        {
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.Fail,
                $"{(verwaist.Count)} Ausnahme(n) zeigen auf Namen, "
                + $"die es nicht mehr gibt: {string.Join(", ", verwaist)}.",
                "Eine Ausnahme ohne Namen ist ein Freibrief, den niemand mehr "
                + "braucht — sie gehört gelöscht, nicht stehengelassen."));
        }

        var hit = namen
            .Where(name => !erlaubt.Any(
                exception => exception.Name.Equals(name, StringComparison.Ordinal)))
            .SelectMany(name => Verboten
                .Where(word => Trifft(name, word))
                .Select(word => $"{name} ({word})"))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToList();

        if (hit.Count > 0)
        {
            return Task.FromResult(Ergebnis(
                SecurityCheckStatus.Fail,
                $"{(hit.Count)} öffentliche(r) Name(n) kündigen "
                + $"eine Zahl über einen Menschen an: {string.Join(", ", hit)}.",
                Remediation));
        }

        return Task.FromResult(Ergebnis(
            SecurityCheckStatus.Pass,
            $"In {(flaechen.Count)} Flächen "
            + $"({string.Join(", ", flaechen.Select(Kurzname))}) kündigt keiner "
            + $"von {(namen.Count)} öffentlichen Namen eine Zahl "
            + $"über einen Menschen an ({(Verboten.Count)} Worte "
            + "geprüft)"
            + (erlaubt.Count > 0
                ? ". Einzeln begründete Homonyme: "
                  + string.Join("; ", erlaubt
                      .OrderBy(exception => exception.Name, StringComparer.Ordinal)
                      .Select(exception => $"{exception.Name} — {exception.Grund}"))
                  + "."
                : "."),
            "Ein Feld, dessen Name eines dieser Worte am Silbenanfang trägt, "
            + "stößt diesen Befund um — und ein Feld, das eine Zahl über einen "
            + "Menschen trägt und unauffällig heißt, ebenfalls: der Name ist das "
            + "Signal, nicht die Regel. Dafür stehen daneben die geschlossenen "
            + "Feldmengen (wt.ki.naht)."));
    }

    /// <summary>Every public type name and every public property.</summary>
    private HashSet<string> Namen() =>
    [
        .. flaechen
            .SelectMany(flaeche => flaeche.GetExportedTypes())
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance
                               | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(eigenschaft => $"{type.Name}.{eigenschaft.Name}")
                .Append(type.Name))
    ];

    /// <summary>Whether a name carries one of the core words at a syllable start.</summary>
    /// <remarks>
    /// <para>Public because it has two readers: this check and the build-time
    /// twin that lets it loose on individual cases. A rebuilt second rule would
    /// be the one to go wrong first — and then it would be green over the one
    /// and red over the other without anything having changed.</para>
    ///
    /// <para>Every syllable of the type name <em>and</em> every syllable of the
    /// member name is checked. Both, because a type <c>Passung</c> with a member
    /// <c>Text</c> is just as wrong as a type <c>Conversation</c> with a member
    /// <c>Passung</c>.</para>
    /// </remarks>
    /// <param name="name">A type name or <c>Type.Member</c>.</param>
    /// <returns>Whether one of the core words strikes.</returns>
    public static bool Trifft(string name) =>
        Kern.Any(word => Trifft(name, word));

    private static bool Trifft(string name, string word) =>
        name.Split('.')
            .SelectMany(Silben)
            .Any(silbe => silbe.StartsWith(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>An identifier, split at its capital letters.</summary>
    private static IEnumerable<string> Silben(string bezeichner) =>
        Zerlegung.Split(bezeichner).Where(silbe => silbe.Length > 0);

    private static readonly Regex Zerlegung =
        new(@"(?<!^)(?=[A-Z0-9])", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>The name of an assembly, without culture and key.</summary>
    private static string Kurzname(Assembly flaeche) =>
        flaeche.GetName().Name ?? "unbenannt";

    private SecurityCheckResult Ergebnis(
        SecurityCheckStatus state, string zusammenfassung, string abhilfe) =>
        new(Id, Module, Category, state, Severity, zusammenfassung, abhilfe)
        {
            References = References
        };
}
