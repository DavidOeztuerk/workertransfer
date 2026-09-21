using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>One résumé per person. The subject id <em>is</em> the key.</summary>
/// <remarks>
/// The aggregate knows neither a visibility nor a recipient. Who may see it is
/// not decided here but in the consent ledger, per company, and a field for it
/// would be a second truth beside the ledger (ADR-0020 §6).
/// <para>
/// There is deliberately no public switch anywhere in this type. A profile can
/// be shown to everybody; a résumé is released to one named company, asked for
/// and answered — see <c>ResumeRequest</c>.
/// </para>
/// </remarks>
public sealed class CurriculumVitae
{
    /// <summary>How many positions a résumé may hold.</summary>
    public const int HoechsteStationen = 40;

    /// <summary>How many education entries a résumé may hold.</summary>
    public const int HoechsteAusbildungen = 20;

    private CurriculumVitae(
        SubjectId subject,
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert)
    {
        Wer = subject;
        Stationen = stationen;
        Ausbildungen = ausbildungen;
        Angelegt = angelegt;
        Geaendert = geaendert;
    }

    /// <summary>Whose résumé this is.</summary>
    public SubjectId Wer { get; }

    /// <summary>Running position first, then newest start first.</summary>
    public IReadOnlyList<Station> Stationen { get; private set; }

    /// <summary>Same order, same reason.</summary>
    public IReadOnlyList<Ausbildung> Ausbildungen { get; private set; }

    /// <summary>When it was first written.</summary>
    public DateTimeOffset Angelegt { get; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset Geaendert { get; private set; }

    /// <summary>In welcher ResumeTemplate er gesetzt wird.</summary>
    /// <remarks>
    /// Ein Name, kein Layout — siehe <see cref="Lebenslaeufe.Vorlage" />. Er
    /// steht am CurriculumVitae und nicht an der JobApplication, weil die Person ihn
    /// einmal wählt und nicht je Unternehmen; und weil sie ihn danach ändern
    /// darf, ohne dass eine abgeschickte JobApplication sich rückwirkend umzieht:
    /// die Mappe zeigt die ResumeTemplate, die zum Zeitpunkt des Lesens gilt, und das
    /// ist die der Person.
    /// </remarks>
    public ResumeTemplate ResumeTemplate { get; private set; } = ResumeTemplate.Plain;

    /// <summary>Wählt die ResumeTemplate.</summary>
    public void Waehle_vorlage(ResumeTemplate template, DateTimeOffset now)
    {
        ResumeTemplate = template;
        Geaendert = now;
    }

    /// <summary>Writes a new one.</summary>
    /// <exception cref="Lebenslaufregel">The entries break a rule of the whole.</exception>
    public static CurriculumVitae Anlegen(
        SubjectId subject,
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset now)
    {
        var (geprueftStationen, geprueftAusbildungen) = Gepruegt(stationen, ausbildungen);

        return new CurriculumVitae(subject, geprueftStationen, geprueftAusbildungen, now, now);
    }

    /// <summary>Rebuilds one that was stored. Checks nothing.</summary>
    /// <remarks>
    /// The repository's way in. Deliberately without the rules: a row written
    /// under an older rule must still be readable, and refusing to load it
    /// would hide somebody's résumé from them because the code changed.
    /// Ordering is applied, because that is derived from the data and not
    /// stored.
    /// </remarks>
    public static CurriculumVitae Wiederherstellen(
        SubjectId subject,
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert,
        ResumeTemplate template = ResumeTemplate.Plain) =>
        new(subject, Sortiert(stationen, s => s.Ende, s => s.Beginn),
            Sortiert(ausbildungen, a => a.Ende, a => a.Beginn), angelegt, geaendert)
        {
            ResumeTemplate = template
        };

    /// <summary>Replaces both lists at once.</summary>
    /// <remarks>
    /// Checked in full before anything is written: a rejected form must not
    /// leave a half-changed aggregate behind.
    /// </remarks>
    /// <exception cref="Lebenslaufregel">The entries break a rule of the whole.</exception>
    public void Aktualisiere(
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset now)
    {
        var (geprueftStationen, geprueftAusbildungen) = Gepruegt(stationen, ausbildungen);

        Stationen = geprueftStationen;
        Ausbildungen = geprueftAusbildungen;
        Geaendert = now;
    }

    /// <summary>
    /// The rules of the whole, in one place so that writing and rewriting
    /// cannot drift apart.
    /// </summary>
    private static (IReadOnlyList<Station>, IReadOnlyList<Ausbildung>) Gepruegt(
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen)
    {
        ArgumentNullException.ThrowIfNull(stationen);
        ArgumentNullException.ThrowIfNull(ausbildungen);

        if (stationen.Count > HoechsteStationen)
        {
            throw new ResumeRule(
                Regelcodes.Menge, $"Höchstens {HoechsteStationen} Stationen sind erlaubt.");
        }

        if (ausbildungen.Count > HoechsteAusbildungen)
        {
            throw new ResumeRule(
                Regelcodes.Menge,
                $"Höchstens {HoechsteAusbildungen} Ausbildungen sind erlaubt.");
        }

        // An open position means "still there". Two of them would say somebody
        // is in two places at once, and the ordering below could not decide
        // which one is now.
        if (stationen.Count(station => station.Laeuft) > 1)
        {
            throw new ResumeRule(
                Regelcodes.ZweiOffene,
                "Nur eine Station darf offen bleiben; offen heißt 'noch dort'.");
        }

        return (Sortiert(stationen, s => s.Ende, s => s.Beginn),
            Sortiert(ausbildungen, a => a.Ende, a => a.Beginn));
    }

    /// <summary>
    /// Running first, then by start descending — derived from the data instead
    /// of from a <c>sort_order</c> column.
    /// </summary>
    /// <remarks>
    /// Such a column would have to be maintained on every edit and would one
    /// day be wrong, and nothing in the interface would show it.
    /// </remarks>
    private static IReadOnlyList<T> Sortiert<T>(
        IReadOnlyList<T> entries,
        Func<T, Monat?> ende,
        Func<T, Monat> beginn) =>
        [.. entries
            .OrderByDescending(entry => ende(entry) is null)
            .ThenByDescending(beginn)];
}
