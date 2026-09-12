using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>Was in einer Unterlage gelesen wurde — auf Auslösung, einmal.</summary>
/// <remarks>
/// <strong>Der Index aus PBI-7, und er lebt hier, weil die Unterlagen hier
/// liegen.</strong> Nicht in profile-service, nicht in scout-service: ein Index
/// über personenbezogene Texte darf überhaupt nur existieren, solange er im
/// selben Dienst steht wie die Dateien, aus denen er stammt — und damit von
/// derselben Löschung getroffen wird (ADR-0027, ADR-0043).
/// <para>
/// <strong>Gespeichert werden Namen, nie der Text.</strong> Der gefundene
/// Volltext lebt im Arbeitsspeicher des einen Aufrufs und wird danach
/// weggeworfen. Eine Spalte mit dem Wortlaut eines Zeugnisses wäre eine zweite
/// Kopie der Unterlage — in der Datenbank statt in der Ablage, und damit in
/// jeder Sicherung und in jedem Abzug, den irgendwer für eine Fehlersuche zieht
/// (dieselbe Begründung wie in <see cref="Unterlage"/>).
/// </para>
/// <para>
/// <strong>Ein Fund ist ein BELEG, keine Nennung</strong> (ADR-0033). Er ist
/// eine Aussage über ein <em>Dokument</em> — dieses Wort steht in dieser Datei
/// — und wird niemals durchsuchbar. Suchbar ist allein, was die Person selbst
/// in ihr Profil getippt und gespeichert hat; zwischen dem Fund und dieser
/// Aussage liegen zwei Handlungen im Browser.
/// </para>
/// </remarks>
public sealed class Unterlagenfund
{
    private readonly List<string> _begriffe;

    private Unterlagenfund(
        Guid id,
        SubjectId wer,
        Guid unterlage,
        bool textGefunden,
        IEnumerable<string> begriffe,
        DateTimeOffset gelesen)
    {
        Id = id;
        Wer = wer;
        Unterlage = unterlage;
        TextGefunden = textGefunden;
        _begriffe = [.. begriffe];
        Gelesen = gelesen;
    }

    /// <summary>Die Kennung dieser Zeile.</summary>
    public Guid Id { get; }

    /// <summary>Wessen Unterlage gelesen wurde.</summary>
    public SubjectId Wer { get; }

    /// <summary>Welche Unterlage.</summary>
    public Guid Unterlage { get; }

    /// <summary>War überhaupt Text zu lesen?</summary>
    /// <remarks>
    /// <strong>Der Unterschied zwischen „nichts gefunden" und „nichts gelesen",
    /// und er ist keine Feinheit.</strong> Ein abfotografierter Gesellenbrief
    /// ist ein Bild: darin steht kein Text, den ein Leser ohne Texterkennung
    /// fände. Beide Fälle als „keine Vorschläge" zu zeigen hiesse, einem
    /// Menschen stillschweigend mitzuteilen, in seinem Meisterbrief stehe
    /// nichts — das ist ADR-0022 §3, Lüge durch Auslassung.
    /// </remarks>
    public bool TextGefunden { get; }

    /// <summary>Die kanonischen Namen, die im Text vorkamen. Darf leer sein.</summary>
    public IReadOnlyList<string> Begriffe => _begriffe;

    /// <summary>Wann gelesen wurde.</summary>
    /// <remarks>
    /// Eine Zeile ohne diesen Zeitpunkt gibt es nicht: „noch nicht gelesen" ist
    /// die <em>Abwesenheit</em> der Zeile, nicht ein leeres Feld darin.
    /// </remarks>
    public DateTimeOffset Gelesen { get; }

    /// <summary>Hält fest, was ein ausgelöster Lesevorgang ergeben hat.</summary>
    /// <param name="wer">Wem die Unterlage gehört.</param>
    /// <param name="unterlage">Welche.</param>
    /// <param name="textGefunden">Ob überhaupt Text zu lesen war.</param>
    /// <param name="begriffe">Die kanonischen Namen aus dem Text.</param>
    /// <param name="jetzt">Der Zeitpunkt.</param>
    public static Unterlagenfund Halte_fest(
        SubjectId wer,
        Guid unterlage,
        bool textGefunden,
        IEnumerable<string> begriffe,
        DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(begriffe);

        return new Unterlagenfund(
            Guid.CreateVersion7(), wer, unterlage, textGefunden, begriffe, jetzt);
    }

    /// <summary>Der Fund, wie eine Zeile ihn hält. Prüft nichts.</summary>
    public static Unterlagenfund Stelle_her(
        Guid id,
        SubjectId wer,
        Guid unterlage,
        bool textGefunden,
        IEnumerable<string> begriffe,
        DateTimeOffset gelesen) =>
        new(id, wer, unterlage, textGefunden, begriffe, gelesen);
}

/// <summary>Führt die Funde. Zwei Fragen, und eine dritte gibt es nicht.</summary>
/// <remarks>
/// <strong>Die Methodenmenge IST die Zusage</strong>, und <c>AuflagenTests</c>
/// vergleicht sie als Menge statt auf Enthaltensein. Gefragt werden kann
/// „was steht in den Unterlagen DIESES Menschen" — nie „welche Menschen haben
/// das Wort X in ihren Unterlagen". Die zweite Frage wäre eine Suche über
/// fremde Dokumente und damit das Gegenteil dieser ganzen Arbeit: durchsuchbar
/// ist allein, was jemand selbst genannt hat (ADR-0033).
/// </remarks>
public interface IFundSpeicher
{
    /// <summary>Die Funde dieser Person.</summary>
    Task<IReadOnlyList<Unterlagenfund>> AlleAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt einen Fund ab und ersetzt einen früheren zu derselben Unterlage.</summary>
    Task SichereAsync(Unterlagenfund fund, CancellationToken cancellationToken = default);

    /// <summary>Nimmt den Fund zu dieser Unterlage weg.</summary>
    /// <remarks>
    /// Für den Weg, auf dem die Unterlage selbst verschwindet: ein Fund ohne
    /// seine Datei wäre der Wortlaut eines gelöschten Zeugnisses, der stehen
    /// bleibt.
    /// </remarks>
    Task LoescheAsync(
        SubjectId wer, Guid unterlage, CancellationToken cancellationToken = default);
}
