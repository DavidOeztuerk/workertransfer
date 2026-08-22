using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>One résumé per person. The subject id <em>is</em> the key.</summary>
/// <remarks>
/// The aggregate knows neither a visibility nor a recipient. Who may see it is
/// not decided here but in the consent ledger, per company, and a field for it
/// would be a second truth beside the ledger (ADR-0020 §6).
/// <para>
/// There is deliberately no public switch anywhere in this type. A profile can
/// be shown to everybody; a résumé is released to one named company, asked for
/// and answered — see <c>Anfrage</c>.
/// </para>
/// </remarks>
public sealed class Lebenslauf
{
    /// <summary>How many positions a résumé may hold.</summary>
    public const int HoechsteStationen = 40;

    /// <summary>How many education entries a résumé may hold.</summary>
    public const int HoechsteAusbildungen = 20;

    private Lebenslauf(
        SubjectId wer,
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert)
    {
        Wer = wer;
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

    /// <summary>Writes a new one.</summary>
    /// <exception cref="Lebenslaufregel">The entries break a rule of the whole.</exception>
    public static Lebenslauf Anlegen(
        SubjectId wer,
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset jetzt)
    {
        var (geprueftStationen, geprueftAusbildungen) = Gepruegt(stationen, ausbildungen);

        return new Lebenslauf(wer, geprueftStationen, geprueftAusbildungen, jetzt, jetzt);
    }

    /// <summary>Rebuilds one that was stored. Checks nothing.</summary>
    /// <remarks>
    /// The repository's way in. Deliberately without the rules: a row written
    /// under an older rule must still be readable, and refusing to load it
    /// would hide somebody's résumé from them because the code changed.
    /// Ordering is applied, because that is derived from the data and not
    /// stored.
    /// </remarks>
    public static Lebenslauf Wiederherstellen(
        SubjectId wer,
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert) =>
        new(wer, Sortiert(stationen, s => s.Ende, s => s.Beginn),
            Sortiert(ausbildungen, a => a.Ende, a => a.Beginn), angelegt, geaendert);

    /// <summary>Replaces both lists at once.</summary>
    /// <remarks>
    /// Checked in full before anything is written: a rejected form must not
    /// leave a half-changed aggregate behind.
    /// </remarks>
    /// <exception cref="Lebenslaufregel">The entries break a rule of the whole.</exception>
    public void Aktualisiere(
        IReadOnlyList<Station> stationen,
        IReadOnlyList<Ausbildung> ausbildungen,
        DateTimeOffset jetzt)
    {
        var (geprueftStationen, geprueftAusbildungen) = Gepruegt(stationen, ausbildungen);

        Stationen = geprueftStationen;
        Ausbildungen = geprueftAusbildungen;
        Geaendert = jetzt;
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
            throw new Lebenslaufregel(
                Regelcodes.Menge, $"Höchstens {HoechsteStationen} Stationen sind erlaubt.");
        }

        if (ausbildungen.Count > HoechsteAusbildungen)
        {
            throw new Lebenslaufregel(
                Regelcodes.Menge,
                $"Höchstens {HoechsteAusbildungen} Ausbildungen sind erlaubt.");
        }

        // An open position means "still there". Two of them would say somebody
        // is in two places at once, and the ordering below could not decide
        // which one is now.
        if (stationen.Count(station => station.Laeuft) > 1)
        {
            throw new Lebenslaufregel(
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
        IReadOnlyList<T> eintraege,
        Func<T, Monat?> ende,
        Func<T, Monat> beginn) =>
        [.. eintraege
            .OrderByDescending(eintrag => ende(eintrag) is null)
            .ThenByDescending(beginn)];
}
