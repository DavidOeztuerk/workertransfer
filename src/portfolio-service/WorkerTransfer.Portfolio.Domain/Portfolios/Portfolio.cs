using Girder.Core.Identity;

namespace WorkerTransfer.Portfolio.Domain.Portfolios;

/// <summary>Was jemand gemacht hat, in seiner eigenen Reihenfolge.</summary>
/// <remarks>
/// Ein Portfolio je Person; die <c>SubjectId</c> <em>ist</em> der Schlüssel.
/// Wer es sehen darf, entscheidet der Consent-Ledger und nicht dieses Modul
/// (ADR-0020 §6) — hier gibt es deshalb kein einziges Sichtbarkeitsfeld.
/// </remarks>
public sealed class Portfolio
{
    /// <summary>Wie viele Einträge ein Portfolio trägt.</summary>
    /// <remarks>
    /// Eine Grenze, keine Bewertung: dreißig Einträge sind ein Schaufenster,
    /// dreihundert wären ein Aktenschrank, und niemand liest ihn.
    /// </remarks>
    public const int HoechsteEintraege = 30;

    private Portfolio(
        SubjectId wer,
        IReadOnlyList<Eintrag> eintraege,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Wer = wer;
        Eintraege = eintraege;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Wessen Portfolio das ist.</summary>
    public SubjectId Wer { get; }

    /// <summary>
    /// Die Einträge, in der Reihenfolge, in der sie kamen.
    /// </summary>
    /// <remarks>
    /// Ein Portfolio hat keine natürliche Ordnung. „Das hier zuerst" ist eine
    /// Entscheidung der Person, und sie umzusortieren hieße, sie zu übergehen.
    /// </remarks>
    public IReadOnlyList<Eintrag> Eintraege { get; private set; }

    /// <summary>Wann es zuerst geschrieben wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann es zuletzt geändert wurde.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <exception cref="ZuVieleEintraege">Es sind zu viele.</exception>
    public static Portfolio Lege_an(
        SubjectId wer, IReadOnlyList<Eintrag> eintraege, DateTimeOffset jetzt) =>
        new(wer, Geprueft(eintraege), jetzt, jetzt);

    /// <summary>Das Portfolio, wie eine Zeile es hält.</summary>
    public static Portfolio Stelle_her(
        SubjectId wer,
        IReadOnlyList<Eintrag> eintraege,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm) =>
        new(wer, eintraege, angelegtAm, geaendertAm);

    /// <summary>Ersetzt die Einträge.</summary>
    /// <remarks>
    /// Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular darf
    /// kein halb geändertes Aggregat hinterlassen.
    /// </remarks>
    /// <exception cref="ZuVieleEintraege">Es sind zu viele.</exception>
    public void Aendere(IReadOnlyList<Eintrag> eintraege, DateTimeOffset jetzt)
    {
        Eintraege = Geprueft(eintraege);
        GeaendertAm = jetzt;
    }

    /// <summary>Ob dieser Anhang zu diesem Portfolio gehört.</summary>
    /// <remarks>
    /// Der Weg zur Datei entsteht aus <c>SubjectId</c> und Name — aber gefragt
    /// wird trotzdem, ob ein Eintrag ihn nennt. Sonst bliebe eine gelöschte
    /// Datei über ihren Namen erreichbar, obwohl sie aus dem Portfolio
    /// verschwunden ist.
    /// </remarks>
    public bool Nennt(string anhang) =>
        Eintraege.Any(eintrag => string.Equals(eintrag.Anhang, anhang, StringComparison.Ordinal));

    private static IReadOnlyList<Eintrag> Geprueft(IReadOnlyList<Eintrag> eintraege)
    {
        ArgumentNullException.ThrowIfNull(eintraege);

        return eintraege.Count > HoechsteEintraege
            ? throw new ZuVieleEintraege(HoechsteEintraege)
            : eintraege;
    }
}

/// <summary>Findet und speichert Portfolios.</summary>
public interface IPortfoliospeicher
{
    /// <summary>Das Portfolio einer Person, oder <c>null</c>.</summary>
    Task<Portfolio?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Portfolio portfolio, CancellationToken cancellationToken = default);

    /// <summary>Löscht alles zu dieser Person.</summary>
    /// <returns>Wie viele Zeilen absichtlich stehen blieben. Immer null.</returns>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
