using Noelia.Core.Identity;

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
    /// Eine Grenze, keine Evaluation: dreißig Einträge sind ein Schaufenster,
    /// dreihundert wären ein Aktenschrank, und niemand liest ihn.
    /// </remarks>
    public const int HoechsteEintraege = 30;

    private Portfolio(
        SubjectId subject,
        IReadOnlyList<Eintrag> entries,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Wer = subject;
        Eintraege = entries;
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
        SubjectId subject, IReadOnlyList<Eintrag> entries, DateTimeOffset now) =>
        new(subject, Geprueft(entries), now, now);

    /// <summary>Das Portfolio, wie eine Zeile es hält.</summary>
    public static Portfolio Stelle_her(
        SubjectId subject,
        IReadOnlyList<Eintrag> entries,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm) =>
        new(subject, entries, angelegtAm, geaendertAm);

    /// <summary>Ersetzt die Einträge.</summary>
    /// <remarks>
    /// Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular darf
    /// kein halb geändertes Aggregat hinterlassen.
    /// </remarks>
    /// <exception cref="ZuVieleEintraege">Es sind zu viele.</exception>
    public void Aendere(IReadOnlyList<Eintrag> entries, DateTimeOffset now)
    {
        Eintraege = Geprueft(entries);
        GeaendertAm = now;
    }

    /// <summary>Ob dieser Anhang zu diesem Portfolio gehört.</summary>
    /// <remarks>
    /// Der Weg zur Datei entsteht aus <c>SubjectId</c> und Name — aber gefragt
    /// wird trotzdem, ob ein Eintrag ihn nennt. Sonst bliebe eine gelöschte
    /// Datei über ihren Namen erreichbar, obwohl sie aus dem Portfolio
    /// verschwunden ist.
    /// </remarks>
    public bool Nennt(string anhang) =>
        Eintraege.Any(entry => string.Equals(entry.Anhang, anhang, StringComparison.Ordinal));

    private static IReadOnlyList<Eintrag> Geprueft(IReadOnlyList<Eintrag> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Count > HoechsteEintraege
            ? throw new ZuVieleEintraege(HoechsteEintraege)
            : entries;
    }
}

/// <summary>Findet und speichert Portfolios.</summary>
public interface IPortfolioStore
{
    /// <summary>Das Portfolio einer Person, oder <c>null</c>.</summary>
    Task<Portfolio?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(Portfolio portfolio, CancellationToken cancellationToken = default);

    /// <summary>Löscht alles zu dieser Person.</summary>
    /// <returns>Wie viele Zeilen absichtlich stehen blieben. Immer null.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
