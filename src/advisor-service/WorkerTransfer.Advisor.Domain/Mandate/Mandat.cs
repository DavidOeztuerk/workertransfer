using System.Globalization;
using Girder.Core.Identity;

namespace WorkerTransfer.Advisor.Domain.Mandate;

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
/// <remarks>
/// Die Meldung nennt die <em>Regel</em> und nie den Wert. Ein Gehalt in einer
/// Fehlermeldung stünde am Ende im Protokoll — und das ist der einzige Ort, an
/// dem es hier nie stehen darf.
/// </remarks>
/// <param name="meldung">Was die Regel verlangt.</param>
public sealed class Eingabefehler(string meldung) : Exception(meldung);

/// <summary>
/// Das Mandat — vier Werte, und alle vier freiwillig.
/// </summary>
/// <remarks>
/// <para><strong>Das Mandat ist eine SICHT, kein zweiter Speicher</strong>
/// (ADR-0037 Entscheidung 1). Wer darf mich sehen, steht im Ledger; ob ich
/// ansprechbar bin, steht im Marktstatus. Beides hier noch einmal zu halten
/// wäre ADR-0020 wörtlich gebrochen — und ein Widerruf müsste dann an zwei
/// Stellen wirken, also irgendwann an einer nicht.</para>
///
/// <para>Übrig bleiben genau die vier Werte, die heute nirgends stehen:
/// Eintrittstermin, Gehaltsspanne, Pensum, ausgeschlossene Unternehmen. Kein
/// Feld heißt <c>sichtbar</c>, <c>visible</c>, <c>public</c> oder
/// <c>freigabe</c>, und ein Test hält das fest.</para>
///
/// <para><strong>Leer ist der Normalfall.</strong> Ein Mandat ohne einen
/// einzigen gefüllten Wert ist vollständig; niemand muss ein Gehalt nennen, um
/// ein Gespräch zu führen. Deshalb gibt es kein „unvollständig" und keine
/// Pflichtangabe.</para>
/// </remarks>
public sealed class Mandat
{
    /// <summary>Wie viele Unternehmen eine Person ausschließen darf.</summary>
    /// <remarks>
    /// Eine Grenze, damit aus der Liste kein Speicher wird, den niemand
    /// aufräumt — großzügig genug, dass niemand sie im Alltag trifft.
    /// </remarks>
    public const int HoechstzahlAusschluesse = 50;

    /// <summary>Wie lang eine Domain sein darf.</summary>
    public const int HoechstlaengeDomain = 253;

    /// <summary>Das kleinste Pensum, das eine Aussage ist.</summary>
    public const int KleinstesPensum = 10;

    /// <summary>Das größte.</summary>
    public const int GroesstesPensum = 100;

    private Mandat(
        SubjectId wer,
        string? eintrittstermin,
        int? gehaltMin,
        int? gehaltMax,
        int? pensumProzent,
        IReadOnlyList<string> ausgeschlossen,
        DateTimeOffset geaendertAm)
    {
        Wer = wer;
        Eintrittstermin = eintrittstermin;
        GehaltMin = gehaltMin;
        GehaltMax = gehaltMax;
        PensumProzent = pensumProzent;
        AusgeschlosseneUnternehmen = ausgeschlossen;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Wessen Mandat. Es <em>ist</em> der Schlüssel.</summary>
    public SubjectId Wer { get; }

    /// <summary>Ab wann, als Monat <c>JJJJ-MM</c> — oder <c>null</c>.</summary>
    /// <remarks>
    /// Ein Monat und kein Datum: „ab März" ist, was ein Mensch mit laufendem
    /// Vertrag sagen kann. Ein Tagesdatum täuschte eine Genauigkeit vor, die
    /// von einer Kündigungsfrist abhängt, die noch niemand gerechnet hat.
    /// </remarks>
    public string? Eintrittstermin { get; }

    /// <summary>Untere Grenze der Spanne, Euro im Monat — oder <c>null</c>.</summary>
    /// <remarks>
    /// Volle Euro und keine Cent. Eine Spanne ist eine <em>Aussage</em>, kein
    /// Betrag, der bewegt wird; zwei Nachkommastellen daran läsen sich wie eine
    /// Rechnung.
    /// </remarks>
    public int? GehaltMin { get; }

    /// <summary>Obere Grenze der Spanne. Siehe <see cref="GehaltMin"/>.</summary>
    public int? GehaltMax { get; }

    /// <summary>Wie viel Prozent einer Vollzeitstelle, 10–100 — oder <c>null</c>.</summary>
    public int? PensumProzent { get; }

    /// <summary>
    /// Unternehmen, mit denen kein Gespräch zustande kommt — als Domains.
    /// </summary>
    /// <remarks>
    /// <para><strong>Diese Liste verlässt den Dienst nie.</strong> Kein
    /// Unternehmen erfährt, dass es darauf steht, und keines erfährt, dass es
    /// eine Liste gibt: ein Gespräch mit einem ausgeschlossenen Unternehmen
    /// scheitert mit derselben 404 wie eines mit einem Menschen, den es nicht
    /// gibt.</para>
    ///
    /// <para><strong>Und sie ist der Grund, warum „alle außer diesen" nicht
    /// existiert.</strong> Der Ledger kennt keine Verneinung; „öffentlich
    /// sichtbar, aber nicht für X" ist darin nicht ausdrückbar. Wer einen
    /// Ausschluss nennt, gibt deshalb <c>profile.visibility:public</c> auf —
    /// das schreibt der Dienst, im Namen der Person, in denselben Ledger. Erst
    /// damit hält die Zusage, dass der jetzige Arbeitgeber die eigene
    /// Belegschaft nicht im Scout sieht.</para>
    /// </remarks>
    public IReadOnlyList<string> AusgeschlosseneUnternehmen { get; }

    /// <summary>Wann zuletzt etwas geändert wurde.</summary>
    public DateTimeOffset GeaendertAm { get; }

    /// <summary>Ob überhaupt etwas gesagt wurde.</summary>
    public bool Leer =>
        Eintrittstermin is null
        && GehaltMin is null
        && GehaltMax is null
        && PensumProzent is null
        && AusgeschlosseneUnternehmen.Count == 0;

    /// <summary>Das leere Mandat. „Nichts gesagt" ist ein Zustand.</summary>
    public static Mandat Leeres(SubjectId wer, DateTimeOffset jetzt) =>
        new(wer, null, null, null, null, [], jetzt);

    /// <summary>Schreibt ein Mandat, nachdem jede Angabe geprüft wurde.</summary>
    /// <remarks>
    /// Erst prüfen, dann bauen: ein abgelehntes Formular darf kein halb
    /// geschriebenes Mandat hinterlassen.
    /// </remarks>
    /// <exception cref="Eingabefehler">Eine Angabe hält die Regel nicht ein.</exception>
    public static Mandat Schreibe(
        SubjectId wer,
        string? eintrittstermin,
        int? gehaltMin,
        int? gehaltMax,
        int? pensumProzent,
        IReadOnlyList<string>? ausgeschlossen,
        DateTimeOffset jetzt)
    {
        var termin = Monat(eintrittstermin);
        var pensum = Pensum(pensumProzent);
        var spanne = Spanne(gehaltMin, gehaltMax);
        var domains = Domains(ausgeschlossen);

        return new Mandat(wer, termin, spanne.Min, spanne.Max, pensum, domains, jetzt);
    }

    /// <summary>Das Mandat, wie eine Zeile es hält.</summary>
    public static Mandat Stelle_her(
        SubjectId wer,
        string? eintrittstermin,
        int? gehaltMin,
        int? gehaltMax,
        int? pensumProzent,
        IReadOnlyList<string> ausgeschlossen,
        DateTimeOffset geaendertAm) =>
        new(wer, eintrittstermin, gehaltMin, gehaltMax, pensumProzent, ausgeschlossen, geaendertAm);

    /// <summary>Schließt dieses Mandat diese Domain aus?</summary>
    /// <remarks>
    /// Genauer Vergleich auf die ganze Domain, kein Suffixtreffer: sonst
    /// schlösse <c>example.com</c> auch <c>notexample.com</c> aus, und der
    /// Mensch erführe nie, warum ein Gespräch nicht zustande kommt.
    /// </remarks>
    public bool Schliesst_aus(string? domain) =>
        domain is { Length: > 0 }
        && AusgeschlosseneUnternehmen.Contains(
            domain.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    private static string? Monat(string? roh)
    {
        var wert = (roh ?? string.Empty).Trim();

        if (wert.Length == 0)
        {
            return null;
        }

        return DateTime.TryParseExact(
            wert, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? wert
            : throw new Eingabefehler("an entry date is a month, written YYYY-MM");
    }

    private static int? Pensum(int? roh) => roh switch
    {
        null => null,
        < KleinstesPensum or > GroesstesPensum => throw new Eingabefehler(
            $"a workload is between {KleinstesPensum} and {GroesstesPensum} percent"),
        _ => roh
    };

    private static (int? Min, int? Max) Spanne(int? min, int? max)
    {
        if (min is < 0 || max is < 0)
        {
            throw new Eingabefehler("a salary is not negative");
        }

        if (min is { } unten && max is { } oben && unten > oben)
        {
            throw new Eingabefehler("the lower bound of a salary range is not above the upper");
        }

        return (min, max);
    }

    private static IReadOnlyList<string> Domains(IReadOnlyList<string>? roh)
    {
        if (roh is null or { Count: 0 })
        {
            return [];
        }

        var sauber = roh
            .Select(eintrag => (eintrag ?? string.Empty).Trim().ToLowerInvariant())
            .Where(eintrag => eintrag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (sauber.Length > HoechstzahlAusschluesse)
        {
            throw new Eingabefehler(
                $"at most {HoechstzahlAusschluesse} excluded companies");
        }

        foreach (var eintrag in sauber)
        {
            if (eintrag.Length > HoechstlaengeDomain
                || !eintrag.Contains('.', StringComparison.Ordinal)
                || eintrag.Contains('@', StringComparison.Ordinal)
                || eintrag.Contains(' ', StringComparison.Ordinal))
            {
                // Die Regel, nie der Wert: eine Domain in einer Meldung stünde
                // am Ende im Protokoll, und sie sagt, wo jemand arbeitet.
                throw new Eingabefehler("an excluded company is named by its domain");
            }
        }

        return sauber;
    }
}

/// <summary>Findet und speichert Mandate.</summary>
public interface IMandatspeicher
{
    /// <summary>Das Mandat dieser Person, oder <c>null</c>.</summary>
    Task<Mandat?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Mandat mandat, CancellationToken cancellationToken = default);
}
