using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Domain.Applications;

/// <summary>Wo ein Entwurf steht.</summary>
/// <remarks>
/// Der Weg ist <c>Entsteht → Pruefen → (Ueberarbeiten ⇄ Pruefen) →
/// Freigegeben → Gesendet</c>, dazu <c>Fehlgeschlagen</c> als Ausgang aus
/// <c>Entsteht</c>. <strong>Es gibt keinen Sprung von Entsteht nach
/// Gesendet</strong>, und es darf keinen geben: zwischen dem Modell und dem
/// Unternehmen stehen zwei Handlungen eines Menschen (ADR-0034).
/// </remarks>
public enum DraftState
{
    /// <summary>Das Modell schreibt gerade.</summary>
    Entsteht,

    /// <summary>Fertig geschrieben, wartet auf einen Menschen.</summary>
    Pruefen,

    /// <summary>Es liegen offene Anmerkungen an.</summary>
    Ueberarbeiten,

    /// <summary>Ein Mensch hat gesagt: so.</summary>
    Freigegeben,

    /// <summary>Als JobApplication hinausgegangen.</summary>
    Gesendet,

    /// <summary>Das Schreiben ist misslungen.</summary>
    Fehlgeschlagen
}

/// <summary>Dieser Schritt geht von hier aus nicht.</summary>
public sealed class DraftStepNotAllowed(DraftState now, string wollte)
    : Eingabefehler($"Ein Entwurf im Stand {now} kann nicht {wollte}.");

/// <summary>Es liegen noch offene Anmerkungen an.</summary>
public sealed class OffeneAnmerkungen()
    : Eingabefehler("Es liegen offene Anmerkungen an — erst überarbeiten.");

/// <summary>Eine Anmerkung am Entwurf.</summary>
/// <remarks>
/// <strong>Das Zitat ist der Unterschied zwischen „gefällt mir nicht" und
/// einem Auftrag.</strong> Wer eine Job markiert, sagt dem Modell, worüber
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

    /// <summary>Die markierte Job, oder leer.</summary>
    public string Zitat { get; }

    /// <summary>Ob eine Überarbeitung sie schon aufgenommen hat.</summary>
    public bool Erledigt { get; private set; }

    /// <summary>Wann sie kam.</summary>
    public DateTimeOffset Angelegt { get; }

    /// <summary>Schreibt eine an.</summary>
    /// <exception cref="Eingabefehler">Text fehlt oder ist zu lang.</exception>
    public static Anmerkung Write(string? text, string? zitat, DateTimeOffset now)
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

        return new Anmerkung(Guid.CreateVersion7(), sauber, markiert, false, now);
    }

    /// <summary>Die Anmerkung, wie eine Zeile sie hält.</summary>
    public static Anmerkung Stelle_her(
        Guid id, string text, string zitat, bool erledigt, DateTimeOffset angelegt) =>
        new(id, text, zitat, erledigt, angelegt);

    /// <summary>Hakt sie ab. Nur die Überarbeitung tut das.</summary>
    internal void Hake_ab() => Erledigt = true;
}

/// <summary>Die Anmerkung taugt nicht.</summary>
public sealed class AnmerkungFehler(string notification) : Eingabefehler(notification);

/// <summary>Ein Anschreiben auf dem Weg zur JobApplication.</summary>
/// <remarks>
/// <strong>Der einzige Ort im System, an dem ein KI-Ergebnis liegen bleibt</strong>
/// — und ADR-0034 sagt, warum das hier richtig und beim Profilentwurf falsch
/// ist: dies ist ein <em>Dokument</em>, das über Tage in Runden entsteht, kein
/// Vorschlag für ein Formularfeld.
/// <para>
/// Was hier <em>nicht</em> steht, ist genauso wichtig: keine Evaluation des
/// Textes, kein Passungswert, keine Reihung der eigenen Applications nach Güte.
/// Das wäre ADR-0022 auf dem Umweg über den eigenen Text.
/// </para>
/// </remarks>
public sealed class ApplicationDraft
{
    /// <summary>Wie lang die Betreffzeile sein darf.</summary>
    public const int HoechstlaengeBetreff = 300;

    /// <summary>Wie lang das Anschreiben sein darf.</summary>
    public const int HoechstlaengeText = 20_000;

    /// <summary>Wie viele Anmerkungen ein Entwurf tragen darf.</summary>
    public const int HoechsteAnmerkungen = 50;

    private readonly List<Anmerkung> _anmerkungen;
    private readonly List<Guid> _unterlagen;

    private ApplicationDraft(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string betreff,
        string text,
        DraftState state,
        int fassung,
        string error,
        bool teiltLebenslauf,
        List<Guid> documents,
        List<Anmerkung> anmerkungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert,
        DateTimeOffset? schreibenBegonnen = null)
    {
        Id = id;
        Job = job;
        Firma = company;
        Wer = subject;
        Betreff = betreff;
        Text = text;
        State = state;
        Fassung = fassung;
        Fehler = error;
        TeiltLebenslauf = teiltLebenslauf;
        _unterlagen = documents;
        _anmerkungen = anmerkungen;
        Angelegt = angelegt;
        Geaendert = geaendert;
        SchreibenBegonnen = schreibenBegonnen;
    }

    /// <summary>Die Kennung.</summary>
    public Guid Id { get; }

    /// <summary>Für welche Job.</summary>
    public Guid Job { get; }

    /// <summary>Welches Unternehmen die Job ausgeschrieben hat.</summary>
    public TenantId Firma { get; }

    /// <summary>Wessen Entwurf.</summary>
    public SubjectId Wer { get; }

    /// <summary>Die Betreffzeile.</summary>
    public string Betreff { get; private set; }

    /// <summary>Das Anschreiben.</summary>
    public string Text { get; private set; }

    /// <summary>Wo er steht.</summary>
    public DraftState State { get; private set; }

    /// <summary>Die wievielte Fassung.</summary>
    /// <remarks>
    /// Sie zählt bei jeder Überarbeitung hoch. Wer nicht sieht, dass sich etwas
    /// geändert hat, prüft nicht wirklich (ADR-0034).
    /// </remarks>
    public int Fassung { get; private set; }

    /// <summary>Warum es misslang — die Art, nie der Inhalt.</summary>
    public string Fehler { get; private set; }

    /// <summary>Ob der CurriculumVitae mitgeht.</summary>
    public bool TeiltLebenslauf { get; private set; }

    /// <summary>Welche Unterlagen mitgehen, in der gewählten Reihenfolge.</summary>
    public IReadOnlyList<Guid> Unterlagen => _unterlagen;

    /// <summary>Die Anmerkungen, älteste zuerst.</summary>
    public IReadOnlyList<Anmerkung> Anmerkungen => _anmerkungen;

    /// <summary>Ob noch etwas offen ist.</summary>
    public bool HatOffeneAnmerkungen => _anmerkungen.Any(entry => !entry.Erledigt);

    /// <summary>Wann er begann.</summary>
    public DateTimeOffset Angelegt { get; }

    /// <summary>Wann zuletzt etwas geschah.</summary>
    public DateTimeOffset Geaendert { get; private set; }

    /// <summary>
    /// Wann dieses Schreiben begann. Unverändert, solange der State Entsteht
    /// ist — sonst springt die Oberfläche bei jedem Bruchstück auf null Sekunden.
    /// </summary>
    public DateTimeOffset? SchreibenBegonnen { get; private set; }

    /// <summary>Beginnt einen Entwurf. Das Modell schreibt noch nicht.</summary>
    public static ApplicationDraft Beginne(
        Guid job, TenantId company, SubjectId subject, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), job, company, subject, string.Empty, string.Empty,
            DraftState.Entsteht, 1, string.Empty, true, [], [], now, now);

    /// <summary>Der Entwurf, wie Zeilen ihn halten. Prüft nichts.</summary>
    public static ApplicationDraft Stelle_her(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string betreff,
        string text,
        DraftState state,
        int fassung,
        string error,
        bool teiltLebenslauf,
        IReadOnlyList<Guid> documents,
        IReadOnlyList<Anmerkung> anmerkungen,
        DateTimeOffset angelegt,
        DateTimeOffset geaendert,
        DateTimeOffset? schreibenBegonnen = null) =>
        new(id, job, company, subject, betreff, text, state, fassung, error,
            teiltLebenslauf, [.. documents],
            [.. anmerkungen.OrderBy(entry => entry.Angelegt)], angelegt, geaendert,
            schreibenBegonnen);

    /// <summary>Startet (noch einmal) das Schreiben — der Brief wird leer.</summary>
    /// <remarks>
    /// Aus Fehlgeschlagen zurück nach Entsteht, damit die Oberfläche „wird
    /// geschrieben" zeigt und das Modell denselben Entwurf füllen darf.
    /// </remarks>
    public void Beginne_schreiben(DateTimeOffset now, bool leeren = true)
    {
        if (State is not (DraftState.Entsteht or DraftState.Fehlgeschlagen
            or DraftState.Ueberarbeiten or DraftState.Pruefen))
        {
            throw new DraftStepNotAllowed(State, "geschrieben werden");
        }

        if (leeren)
        {
            Betreff = string.Empty;
            Text = string.Empty;
        }

        Fehler = string.Empty;
        State = DraftState.Entsteht;
        // Schon laufendes Schreiben behält den Start — sonst springt die
        // Oberfläche bei einem zweiten Klick oder einem Poll auf null Sekunden.
        if (SchreibenBegonnen is null)
        {
            SchreibenBegonnen = now;
        }

        Geaendert = now;
    }

    /// <summary>Ein Zwischenstand, während das Modell noch schreibt.</summary>
    /// <remarks>
    /// None neue Fassung, kein Sprung nach Prüfen — sonst wäre ein halber
    /// Brief freigabefähig.
    /// </remarks>
    public void Nimm_bruchstueck(string? betreff, string? text, DateTimeOffset now)
    {
        if (State != DraftState.Entsteht)
        {
            throw new DraftStepNotAllowed(State, "geschrieben werden");
        }

        Setze_text(betreff, text);
        Geaendert = now;
    }

    /// <summary>Das Modell hat geschrieben.</summary>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er entstand gar nicht.</exception>
    public void Nimm_text_an(string? betreff, string? text, DateTimeOffset now)
    {
        // Auch aus Fehlgeschlagen: „noch einmal schreiben" ist derselbe
        // Schritt, nur nach einem misslungenen Versuch. Ihn zu verbieten
        // verwirft eine fertige Modellantwort als 422.
        if (State is not (DraftState.Entsteht or DraftState.Fehlgeschlagen))
        {
            throw new DraftStepNotAllowed(State, "geschrieben werden");
        }

        Setze_text(betreff, text);
        State = DraftState.Pruefen;
        Fehler = string.Empty;
        SchreibenBegonnen = null;
        Geaendert = now;
    }

    /// <summary>Das Schreiben ist misslungen.</summary>
    /// <remarks>
    /// <c>grund</c> nennt die ART, nie den Inhalt — dieselbe Regel wie überall
    /// sonst: „der Anbieter antwortet nicht", nicht der halbe Text.
    /// </remarks>
    public void Scheitere(string reason, DateTimeOffset now)
    {
        State = DraftState.Fehlgeschlagen;
        Fehler = (reason ?? string.Empty).Trim();
        SchreibenBegonnen = null;
        Geaendert = now;
    }

    /// <summary>Die Person ändert selbst.</summary>
    /// <remarks>
    /// Erlaubt in <c>Pruefen</c> und <c>Ueberarbeiten</c>, nicht danach: an
    /// einem freigegebenen Text zu schreiben, ohne die Release zu verlieren,
    /// hiesse, dass „freigegeben" nichts über den Text aussagt. Deshalb fällt
    /// der State hier auf <c>Pruefen</c> zurück.
    /// </remarks>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist schon hinaus.</exception>
    public void Aendere_selbst(string? betreff, string? text, DateTimeOffset now)
    {
        if (State is DraftState.Gesendet or DraftState.Entsteht)
        {
            throw new DraftStepNotAllowed(State, "geändert werden");
        }

        Setze_text(betreff, text);
        State = HatOffeneAnmerkungen ? DraftState.Ueberarbeiten : DraftState.Pruefen;
        Geaendert = now;
    }

    /// <summary>Wählt, was mitgeht.</summary>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist schon hinaus.</exception>
    public void Waehle_beilagen(
        bool resume, IReadOnlyList<Guid> documents, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (State == DraftState.Gesendet)
        {
            throw new DraftStepNotAllowed(State, "geändert werden");
        }

        TeiltLebenslauf = resume;
        _unterlagen.Clear();
        // Reihenfolge der Wahl, doppelte raus: die Mappe zeigt Reiter, und
        // zweimal derselbe Reiter waere ein Fehler, den niemand erklaeren kann.
        _unterlagen.AddRange(documents.Distinct());
        Geaendert = now;
    }

    /// <summary>Merkt etwas an — und damit ist der Entwurf nicht mehr fertig.</summary>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist schon hinaus.</exception>
    public Anmerkung Merke_an(string? text, string? zitat, DateTimeOffset now)
    {
        if (State is DraftState.Gesendet or DraftState.Entsteht)
        {
            throw new DraftStepNotAllowed(State, "kommentiert werden");
        }

        if (_anmerkungen.Count >= HoechsteAnmerkungen)
        {
            throw new AnmerkungFehler(
                $"Mehr als {HoechsteAnmerkungen} Anmerkungen trägt ein Entwurf nicht.");
        }

        var anmerkung = Anmerkung.Write(text, zitat, now);

        _anmerkungen.Add(anmerkung);

        // AUCH AUS `Freigegeben` ZURÜCK. Wer nach der Release noch etwas
        // findet, hat es gefunden — und eine Release, die einen Widerspruch
        // überlebt, ist keine.
        State = DraftState.Ueberarbeiten;
        Geaendert = now;

        return anmerkung;
    }

    /// <summary>Was das Modell umsetzen soll.</summary>
    public IReadOnlyList<Anmerkung> OffeneAnmerkungen =>
        [.. _anmerkungen.Where(entry => !entry.Erledigt)];

    /// <summary>Das Modell hat überarbeitet.</summary>
    /// <remarks>
    /// <strong>Ohne offene Anmerkung geschieht das nicht.</strong> Ein Knopf,
    /// der ohne Auftrag umschreibt, wäre die Reflexionsstufe aus ADR-0024 unter
    /// anderem Namen — zwei Aufrufe, die die Worte der Person ungefragt
    /// verändern.
    /// </remarks>
    /// <exception cref="OffeneAnmerkungen">Es gibt keinen Auftrag.</exception>
    public void Ueberarbeite(string? betreff, string? text, DateTimeOffset now)
    {
        if (State == DraftState.Gesendet)
        {
            throw new DraftStepNotAllowed(State, "überarbeitet werden");
        }

        if (!HatOffeneAnmerkungen)
        {
            throw new OffeneAnmerkungen();
        }

        Setze_text(betreff, text);

        foreach (var anmerkung in _anmerkungen.Where(entry => !entry.Erledigt))
        {
            anmerkung.Hake_ab();
        }

        Fassung++;
        State = DraftState.Pruefen;
        SchreibenBegonnen = null;
        Geaendert = now;
    }

    /// <summary>So, und nicht anders.</summary>
    /// <exception cref="OffeneAnmerkungen">Es steht noch etwas aus.</exception>
    public void Gib_frei(DateTimeOffset now)
    {
        if (State is not (DraftState.Pruefen or DraftState.Ueberarbeiten))
        {
            throw new DraftStepNotAllowed(State, "freigegeben werden");
        }

        if (HatOffeneAnmerkungen)
        {
            throw new OffeneAnmerkungen();
        }

        if (Text.Length == 0)
        {
            throw new AnmerkungFehler("Ein leeres Anschreiben gibt niemand frei.");
        }

        State = DraftState.Freigegeben;
        Geaendert = now;
    }

    /// <summary>Hinaus.</summary>
    /// <remarks>
    /// <strong>Nur aus <c>Freigegeben</c>.</strong> Freigeben und Senden sind
    /// zwei Handlungen; ein Knopf „freigeben und senden" spart einen Klick und
    /// nimmt der Release ihren Sinn (ADR-0034).
    /// </remarks>
    /// <exception cref="EntwurfsschrittNichtErlaubt">Er ist nicht freigegeben.</exception>
    public void Vermerke_versand(DateTimeOffset now)
    {
        if (State != DraftState.Freigegeben)
        {
            throw new DraftStepNotAllowed(State, "gesendet werden");
        }

        State = DraftState.Gesendet;
        Geaendert = now;
    }

    private void Setze_text(string? betreff, string? text)
    {
        var row = (betreff ?? string.Empty).Trim();
        var body = (text ?? string.Empty).Trim();

        if (row.Length > HoechstlaengeBetreff)
        {
            row = row[..HoechstlaengeBetreff];
        }

        if (body.Length > HoechstlaengeText)
        {
            throw new AnmerkungFehler(
                $"Das Anschreiben ist länger als {HoechstlaengeText} Zeichen.");
        }

        Betreff = row;
        Text = body;
    }
}

/// <summary>Findet und speichert Entwürfe.</summary>
public interface IDraftStore
{
    /// <summary>Die Entwürfe dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<ApplicationDraft>> MineAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Einer, oder <c>null</c>.</summary>
    Task<ApplicationDraft?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Der offene Entwurf zu dieser Job, oder <c>null</c>.</summary>
    /// <remarks>
    /// „Offen" heißt: noch nicht gesendet. Zweimal für dieselbe Job zu
    /// entwerfen, ohne es zu merken, kostet einen Modellaufruf und verwirrt die
    /// Liste.
    /// </remarks>
    Task<ApplicationDraft?> OpenOneAsync(
        SubjectId subject, Guid job, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(
        ApplicationDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Löscht einen.</summary>
    Task DeleteAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default);
}
