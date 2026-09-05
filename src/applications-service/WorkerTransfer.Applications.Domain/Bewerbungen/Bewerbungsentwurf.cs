using Girder.Core.Identity;

namespace WorkerTransfer.Applications.Domain.Bewerbungen;

/// <summary>Wo ein Entwurf steht.</summary>
/// <remarks>
/// Der Weg ist <c>Entsteht → Pruefen → (Ueberarbeiten ⇄ Pruefen) →
/// Freigegeben → Gesendet</c>, dazu <c>Fehlgeschlagen</c> als Ausgang aus
/// <c>Entsteht</c>. <strong>Es gibt keinen Sprung von Entsteht nach
/// Gesendet</strong>, und es darf keinen geben: zwischen dem Modell und dem
/// Unternehmen stehen zwei Handlungen eines Menschen (ADR-0034).
/// </remarks>
public enum Entwurfsstand
{
    /// <summary>Das Modell schreibt gerade.</summary>
    Entsteht,

    /// <summary>Fertig geschrieben, wartet auf einen Menschen.</summary>
    Pruefen,

    /// <summary>Es liegen offene Anmerkungen an.</summary>
    Ueberarbeiten,

    /// <summary>Ein Mensch hat gesagt: so.</summary>
    Freigegeben,

    /// <summary>Als Bewerbung hinausgegangen.</summary>
    Gesendet,

    /// <summary>Das Schreiben ist misslungen.</summary>
    Fehlgeschlagen
}

/// <summary>Dieser Schritt geht von hier aus nicht.</summary>
public sealed class EntwurfsschrittNichtErlaubt(Entwurfsstand jetzt, string wollte)
    : Eingabefehler($"Ein Entwurf im Stand {jetzt} kann nicht {wollte}.");

/// <summary>Es liegen noch offene Anmerkungen an.</summary>
public sealed class OffeneAnmerkungen()
    : Eingabefehler("Es liegen offene Anmerkungen an — erst überarbeiten.");

/// <summary>Eine Anmerkung am Entwurf.</summary>
/// <remarks>
/// <strong>Das Zitat ist der Unterschied zwischen „gefällt mir nicht" und
/// einem Auftrag.</strong> Wer eine Stelle markiert, sagt dem Modell, worüber
/// er spricht — und dem nächsten Leser, worauf sich die Anmerkung bezog, auch
/// wenn der Text sich inzwischen geändert hat.
/// </remarks>
public sealed class Anmerkung
{
    /// <summary>Wie lang eine Anmerkung sein darf.</summary>
    public const int HoechstlaengeText = 1_000;

    /// <summary>Wie lang das Zitat sein darf.</summary>
    public const int HoechstlaengeZitat = 500;

    private Anmerkung(
        Guid id, string text, string zitat, bool erledigt, DateTimeOffset angelegt)
    {
        Id = id;
        Text = text;
        Zitat = zitat;
        Erledigt = erledigt;
        Angelegt = angelegt;
    }

    /// <summary>Die Kennung.</summary>
    public Guid Id { get; }

    /// <summary>Was der Mensch geschrieben hat.</summary>
    public string Text { get; }

    /// <summary>Die markierte Stelle, oder leer.</summary>
    public string Zitat { get; }

    /// <summary>Ob eine Überarbeitung sie schon aufgenommen hat.</summary>
    public bool Erledigt { get; private set; }

    /// <summary>Wann sie kam.</summary>
    public DateTimeOffset Angelegt { get; }

    /// <summary>Schreibt eine an.</summary>
    /// <exception cref="Eingabefehler">Text fehlt oder ist zu lang.</exception>
    public static Anmerkung Schreibe(string? text, string? zitat, DateTimeOffset jetzt)
    {
        var sauber = (text ?? string.Empty).Trim();

        if (sauber.Length == 0)
        {
            throw new AnmerkungFehler("Eine Anmerkung ohne Text sagt nichts.");
        }

        if (sauber.Length > HoechstlaengeText)
        {
            throw new AnmerkungFehler(
                $"Die Anmerkung ist länger als {HoechstlaengeText} Zeichen.");
        }

        var markiert = (zitat ?? string.Empty).Trim();

        if (markiert.Length > HoechstlaengeZitat)
        {
            markiert = markiert[..HoechstlaengeZitat];
        }

        return new Anmerkung(Guid.CreateVersion7(), sauber, markiert, false, jetzt);
    }

    /// <summary>Die Anmerkung, wie eine Zeile sie hält.</summary>
    public static Anmerkung Stelle_her(
        Guid id, string text, string zitat, bool erledigt, DateTimeOffset angelegt) =>
        new(id, text, zitat, erledigt, angelegt);

    /// <summary>Hakt sie ab. Nur die Überarbeitung tut das.</summary>
    internal void Hake_ab() => Erledigt = true;
}

/// <summary>Die Anmerkung taugt nicht.</summary>
public sealed class AnmerkungFehler(string meldung) : Eingabefehler(meldung);

/// <summary>Ein Anschreiben auf dem Weg zur Bewerbung.</summary>
/// <remarks>
/// <strong>Der einzige Ort im System, an dem ein KI-Ergebnis liegen bleibt</strong>
/// — und ADR-0034 sagt, warum das hier richtig und beim Profilentwurf falsch
/// ist: dies ist ein <em>Dokument</em>, das über Tage in Runden entsteht, kein
/// Vorschlag für ein Formularfeld.
/// <para>
/// Was hier <em>nicht</em> steht, ist genauso wichtig: keine Bewertung des
/// Textes, kein Passungswert, keine Reihung der eigenen Bewerbungen nach Güte.
/// Das wäre ADR-0022 auf dem Umweg über den eigenen Text.
/// </para>
/// </remarks>
public sealed class Bewerbungsentwurf
{
    /// <summary>Wie lang die Betreffzeile sein darf.</summary>
    public const int HoechstlaengeBetreff = 300;

    /// <summary>Wie lang das Anschreiben sein darf.</summary>
    public const int HoechstlaengeText = 20_000;

    /// <summary>Wie viele Anmerkungen ein Entwurf tragen darf.</summary>
    public const int HoechsteAnmerkungen = 50;

    private readonly List<Anmerkung> _anmerkungen;
    private readonly List<Guid> _unterlagen;

    private Bewerbungsentwurf(
        Guid id,
        Guid stelle,
        TenantId firma,
        SubjectId wer,
        string betreff,
        string text,
        Entwurfsstand stand,
        int fassung,
        string fehler,
        bool teiltLebenslauf,
        List<Guid> unterlagen,
        List<Anmerkung> anmerkungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert)
    {
        Id = id;
        Stelle = stelle;
        Firma = firma;
        Wer = wer;
        Betreff = betreff;
        Text = text;
        Stand = stand;
        Fassung = fassung;
        Fehler = fehler;
        TeiltLebenslauf = teiltLebenslauf;
        _unterlagen = unterlagen;
        _anmerkungen = anmerkungen;
        Angelegt = angelegt;
        Geaendert = geaendert;
    }

    /// <summary>Die Kennung.</summary>
    public Guid Id { get; }

    /// <summary>Für welche Stelle.</summary>
    public Guid Stelle { get; }

    /// <summary>Welches Unternehmen die Stelle ausgeschrieben hat.</summary>
    public TenantId Firma { get; }

    /// <summary>Wessen Entwurf.</summary>
    public SubjectId Wer { get; }

    /// <summary>Die Betreffzeile.</summary>
    public string Betreff { get; private set; }

    /// <summary>Das Anschreiben.</summary>
    public string Text { get; private set; }

    /// <summary>Wo er steht.</summary>
    public Entwurfsstand Stand { get; private set; }

    /// <summary>Die wievielte Fassung.</summary>
    /// <remarks>
    /// Sie zählt bei jeder Überarbeitung hoch. Wer nicht sieht, dass sich etwas
    /// geändert hat, prüft nicht wirklich (ADR-0034).
    /// </remarks>
    public int Fassung { get; private set; }

    /// <summary>Warum es misslang — die Art, nie der Inhalt.</summary>
    public string Fehler { get; private set; }

    /// <summary>Ob der Lebenslauf mitgeht.</summary>
    public bool TeiltLebenslauf { get; private set; }

    /// <summary>Welche Unterlagen mitgehen, in der gewählten Reihenfolge.</summary>
    public IReadOnlyList<Guid> Unterlagen => _unterlagen;

    /// <summary>Die Anmerkungen, älteste zuerst.</summary>
    public IReadOnlyList<Anmerkung> Anmerkungen => _anmerkungen;

    /// <summary>Ob noch etwas offen ist.</summary>
    public bool HatOffeneAnmerkungen => _anmerkungen.Any(eintrag => !eintrag.Erledigt);

    /// <summary>Wann er begann.</summary>
    public DateTimeOffset Angelegt { get; }

    /// <summary>Wann zuletzt etwas geschah.</summary>
    public DateTimeOffset Geaendert { get; private set; }

    /// <summary>Beginnt einen Entwurf. Das Modell schreibt noch nicht.</summary>
    public static Bewerbungsentwurf Beginne(
        Guid stelle, TenantId firma, SubjectId wer, DateTimeOffset jetzt) =>
        new(Guid.CreateVersion7(), stelle, firma, wer, string.Empty, string.Empty,
            Entwurfsstand.Entsteht, 1, string.Empty, false, [], [], jetzt, jetzt);

    /// <summary>Der Entwurf, wie Zeilen ihn halten. Prüft nichts.</summary>
    public static Bewerbungsentwurf Stelle_her(
        Guid id,
        Guid stelle,
        TenantId firma,
        SubjectId wer,
        string betreff,
        string text,
        Entwurfsstand stand,
        int fassung,
        string fehler,
        bool teiltLebenslauf,
        IReadOnlyList<Guid> unterlagen,
        IReadOnlyList<Anmerkung> anmerkungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert) =>
        new(id, stelle, firma, wer, betreff, text, stand, fassung, fehler,
            teiltLebenslauf, [.. unterlagen],
            [.. anmerkungen.OrderBy(eintrag => eintrag.Angelegt)], angelegt, geaendert);

    /// <summary>Das Modell hat geschrieben.</summary>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er entstand gar nicht.</exception>
    public void Nimm_text_an(string? betreff, string? text, DateTimeOffset jetzt)
    {
        if (Stand != Entwurfsstand.Entsteht)
        {
            throw new EntwurfsschrittNichtErlaubt(Stand, "geschrieben werden");
        }

        Setze_text(betreff, text);
        Stand = Entwurfsstand.Pruefen;
        Geaendert = jetzt;
    }

    /// <summary>Das Schreiben ist misslungen.</summary>
    /// <remarks>
    /// <c>grund</c> nennt die ART, nie den Inhalt — dieselbe Regel wie überall
    /// sonst: „der Anbieter antwortet nicht", nicht der halbe Text.
    /// </remarks>
    public void Scheitere(string grund, DateTimeOffset jetzt)
    {
        Stand = Entwurfsstand.Fehlgeschlagen;
        Fehler = (grund ?? string.Empty).Trim();
        Geaendert = jetzt;
    }

    /// <summary>Die Person ändert selbst.</summary>
    /// <remarks>
    /// Erlaubt in <c>Pruefen</c> und <c>Ueberarbeiten</c>, nicht danach: an
    /// einem freigegebenen Text zu schreiben, ohne die Freigabe zu verlieren,
    /// hiesse, dass „freigegeben" nichts über den Text aussagt. Deshalb fällt
    /// der Stand hier auf <c>Pruefen</c> zurück.
    /// </remarks>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist schon hinaus.</exception>
    public void Aendere_selbst(string? betreff, string? text, DateTimeOffset jetzt)
    {
        if (Stand is Entwurfsstand.Gesendet or Entwurfsstand.Entsteht)
        {
            throw new EntwurfsschrittNichtErlaubt(Stand, "geändert werden");
        }

        Setze_text(betreff, text);
        Stand = HatOffeneAnmerkungen ? Entwurfsstand.Ueberarbeiten : Entwurfsstand.Pruefen;
        Geaendert = jetzt;
    }

    /// <summary>Wählt, was mitgeht.</summary>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist schon hinaus.</exception>
    public void Waehle_beilagen(
        bool lebenslauf, IReadOnlyList<Guid> unterlagen, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(unterlagen);

        if (Stand == Entwurfsstand.Gesendet)
        {
            throw new EntwurfsschrittNichtErlaubt(Stand, "geändert werden");
        }

        TeiltLebenslauf = lebenslauf;
        _unterlagen.Clear();
        // Reihenfolge der Wahl, doppelte raus: die Mappe zeigt Reiter, und
        // zweimal derselbe Reiter waere ein Fehler, den niemand erklaeren kann.
        _unterlagen.AddRange(unterlagen.Distinct());
        Geaendert = jetzt;
    }

    /// <summary>Merkt etwas an — und damit ist der Entwurf nicht mehr fertig.</summary>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist schon hinaus.</exception>
    public Anmerkung Merke_an(string? text, string? zitat, DateTimeOffset jetzt)
    {
        if (Stand is Entwurfsstand.Gesendet or Entwurfsstand.Entsteht)
        {
            throw new EntwurfsschrittNichtErlaubt(Stand, "kommentiert werden");
        }

        if (_anmerkungen.Count >= HoechsteAnmerkungen)
        {
            throw new AnmerkungFehler(
                $"Mehr als {HoechsteAnmerkungen} Anmerkungen trägt ein Entwurf nicht.");
        }

        var anmerkung = Anmerkung.Schreibe(text, zitat, jetzt);

        _anmerkungen.Add(anmerkung);

        // AUCH AUS `Freigegeben` ZURÜCK. Wer nach der Freigabe noch etwas
        // findet, hat es gefunden — und eine Freigabe, die einen Widerspruch
        // überlebt, ist keine.
        Stand = Entwurfsstand.Ueberarbeiten;
        Geaendert = jetzt;

        return anmerkung;
    }

    /// <summary>Was das Modell umsetzen soll.</summary>
    public IReadOnlyList<Anmerkung> OffeneAnmerkungen =>
        [.. _anmerkungen.Where(eintrag => !eintrag.Erledigt)];

    /// <summary>Das Modell hat überarbeitet.</summary>
    /// <remarks>
    /// <strong>Ohne offene Anmerkung geschieht das nicht.</strong> Ein Knopf,
    /// der ohne Auftrag umschreibt, wäre die Reflexionsstufe aus ADR-0024 unter
    /// anderem Namen — zwei Aufrufe, die die Worte der Person ungefragt
    /// verändern.
    /// </remarks>
    /// <exception cref="OffeneAnmerkungen">Es gibt keinen Auftrag.</exception>
    public void Ueberarbeite(string? betreff, string? text, DateTimeOffset jetzt)
    {
        if (Stand is Entwurfsstand.Gesendet or Entwurfsstand.Entsteht)
        {
            throw new EntwurfsschrittNichtErlaubt(Stand, "überarbeitet werden");
        }

        if (!HatOffeneAnmerkungen)
        {
            throw new OffeneAnmerkungen();
        }

        Setze_text(betreff, text);

        foreach (var anmerkung in _anmerkungen.Where(eintrag => !eintrag.Erledigt))
        {
            anmerkung.Hake_ab();
        }

        Fassung++;
        Stand = Entwurfsstand.Pruefen;
        Geaendert = jetzt;
    }

    /// <summary>So, und nicht anders.</summary>
    /// <exception cref="OffeneAnmerkungen">Es steht noch etwas aus.</exception>
    public void Gib_frei(DateTimeOffset jetzt)
    {
        if (Stand is not (Entwurfsstand.Pruefen or Entwurfsstand.Ueberarbeiten))
        {
            throw new EntwurfsschrittNichtErlaubt(Stand, "freigegeben werden");
        }

        if (HatOffeneAnmerkungen)
        {
            throw new OffeneAnmerkungen();
        }

        if (Text.Length == 0)
        {
            throw new AnmerkungFehler("Ein leeres Anschreiben gibt niemand frei.");
        }

        Stand = Entwurfsstand.Freigegeben;
        Geaendert = jetzt;
    }

    /// <summary>Hinaus.</summary>
    /// <remarks>
    /// <strong>Nur aus <c>Freigegeben</c>.</strong> Freigeben und Senden sind
    /// zwei Handlungen; ein Knopf „freigeben und senden" spart einen Klick und
    /// nimmt der Freigabe ihren Sinn (ADR-0034).
    /// </remarks>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist nicht freigegeben.</exception>
    public void Vermerke_versand(DateTimeOffset jetzt)
    {
        if (Stand != Entwurfsstand.Freigegeben)
        {
            throw new EntwurfsschrittNichtErlaubt(Stand, "gesendet werden");
        }

        Stand = Entwurfsstand.Gesendet;
        Geaendert = jetzt;
    }

    private void Setze_text(string? betreff, string? text)
    {
        var zeile = (betreff ?? string.Empty).Trim();
        var rumpf = (text ?? string.Empty).Trim();

        if (zeile.Length > HoechstlaengeBetreff)
        {
            zeile = zeile[..HoechstlaengeBetreff];
        }

        if (rumpf.Length > HoechstlaengeText)
        {
            throw new AnmerkungFehler(
                $"Das Anschreiben ist länger als {HoechstlaengeText} Zeichen.");
        }

        Betreff = zeile;
        Text = rumpf;
    }
}

/// <summary>Findet und speichert Entwürfe.</summary>
public interface IEntwurfsspeicher
{
    /// <summary>Die Entwürfe dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<Bewerbungsentwurf>> MeineAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Einer, oder <c>null</c>.</summary>
    Task<Bewerbungsentwurf?> HoleAsync(
        SubjectId wer, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Der offene Entwurf zu dieser Stelle, oder <c>null</c>.</summary>
    /// <remarks>
    /// „Offen" heißt: noch nicht gesendet. Zweimal für dieselbe Stelle zu
    /// entwerfen, ohne es zu merken, kostet einen Modellaufruf und verwirrt die
    /// Liste.
    /// </remarks>
    Task<Bewerbungsentwurf?> OffenerAsync(
        SubjectId wer, Guid stelle, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(
        Bewerbungsentwurf entwurf, CancellationToken cancellationToken = default);

    /// <summary>Löscht einen.</summary>
    Task LoescheAsync(
        SubjectId wer, Guid id, CancellationToken cancellationToken = default);
}
