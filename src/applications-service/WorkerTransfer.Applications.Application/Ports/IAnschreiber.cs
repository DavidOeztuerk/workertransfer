using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Es ist kein Anbieter eingerichtet — oder er antwortet nicht.</summary>
public sealed class CoverLetterUnavailable(string notification) : Exception(notification);

/// <summary>Eine Station aus dem eigenen CurriculumVitae, wie das Modell sie sieht.</summary>
public sealed record Werdegangstation(
    string Arbeitgeber,
    string Titel,
    string Zeitraum,
    string Beschreibung,
    IReadOnlyList<string> Technologien);

/// <summary>
/// Was in den Prompt darf — und damit auch: was nicht (ADR-0034).
/// </summary>
/// <remarks>
/// <strong>Diese Klasse IST die Grenze.</strong> Sie trägt zwei Dinge und
/// nichts Drittes:
/// <list type="number">
///   <item>die <em>eigenen</em> Angaben der bewerbenden Person,</item>
///   <item>die <em>öffentliche</em> Stellenanzeige.</item>
/// </list>
/// <para>
/// <strong>Nie eine dritte Person.</strong> None anderen Bewerber, keine
/// Vergleichszahlen, keine Einschätzung — nichts, was eine Aussage
/// <em>über</em> jemanden wäre. Ein Test hält die Feldliste fest, so wie
/// ADR-0024 es für die anderen beiden Verbraucher tut.
/// </para>
/// <para>
/// <strong>Der Firmenname darf hier stehen</strong>, anders als bei
/// <c>AdvertDraft</c>. Dort wäre er der Anfang einer Aussage über ein
/// Unternehmen; hier ist er die PostalAddress. Man kann keinen Brief schreiben,
/// ohne zu wissen, an wen.
/// </para>
/// </remarks>
public sealed record CoverLetterContext(
    string StellenTitel,
    string Unternehmen,
    string StellenOrt,
    string StellenBeschreibung,
    IReadOnlyList<string> GesuchteFaehigkeiten,
    string EigenerName,
    string EigeneUeberschrift,
    string EigenerText,
    IReadOnlyList<string> EigeneFaehigkeiten,
    IReadOnlyList<Werdegangstation> EigenerWerdegang,
    string Sprache)
{
    /// <summary>Die Regeln, die über jedem Aufruf stehen.</summary>
    /// <remarks>
    /// <strong>„Erfinde nichts" ist keine Höflichkeit.</strong> Ein erfundener
    /// Satz im Anschreiben ist eine Falschangabe, für die die Person haftet und
    /// nicht die Plattform. Und die Regel gegen Selbstbewertung ist ADR-0022 an
    /// der Job, an der sie am leichtesten zu übersehen ist: „ich bin zu 90 %
    /// geeignet" wäre eine Zahl über einen Menschen, nur aus seiner eigenen
    /// Feder.
    /// </remarks>
    public const string Regeln = """
        Du schreibst ein Bewerbungsanschreiben in der Ich-Form.

        Sprache: genau die angegebene (de, en oder fr). Write wie ein
        Muttersprachler. Nur vollstaendige, grammatisch korrekte Saetze.
        None Wortschoepfungen, keine falschen Verbformen, keine woertlich
        uebersetzten Floskeln, keine halben Konstruktionen.

        1. Verwende ausschliesslich die angegebenen Angaben der Person. Erfinde
           KEINE Qualifikationen, keine Arbeitgeber, keine Zeitraeume, keine
           Abschluesse, keine Orte, keine Familie und keine Zahlen. Was nicht
           dasteht, existiert nicht — auch keinen Hof, keine Oma, keine Tools.
        2. Ist der Werdegang dünn: erfinde nichts. Schreib trotzdem einen
           vollstaendigen Brief — Anrede, zwei bis vier Absaetze, Schluss —
           aus Ueberschrift, Text, Faehigkeiten und der Stellenanzeige.
        3. Beziehe dich konkret auf die Stellenbeschreibung. Belege nur mit
           Angaben, die dastehen.
        4. None Selbstbewertung in Zahlen — kein Prozentwert, keine Note, kein
           "zu 90 % geeignet".
        5. None Floskeln wie "hiermit bewerbe ich mich".
        6. Haupttext 180 bis 250 Woerter — eine Seite, nicht ein duenner Absatz.
        7. Die Signatur ist GENAU der volle Name aus den Angaben, kein Spitzname
           und kein anderer Name.

        Ausgabeformat, genau so, vollstaendige Woerter, kein fehlender Buchstabe:
        BETREFF: <Betreffzeile>
        ---
        <Anschreiben mit Anrede, Absaetzen und Gruss, ohne Briefkopf>
        """;
}

/// <summary>Entwirft ein Anschreiben — auf Bitte, und nur dann.</summary>
public interface ICoverLetterWriter
{
    /// <summary>Schreibt. Behält nichts.</summary>
    /// <exception cref="AnschreibenNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    Task<string> WriteAsync(
        KiZugang zugang,
        CoverLetterContext context,
        Func<string, Task>? fortschritt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Überarbeitet anhand der Anmerkungen.</summary>
    /// <remarks>
    /// Die Anmerkungen sind der ganze Auftrag — wörtlich, samt Zitat, und mit
    /// der Anweisung, alles Unkommentierte zu lassen.
    /// </remarks>
    /// <exception cref="AnschreibenNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    Task<string> ReviseAsync(
        KiZugang zugang,
        CoverLetterContext context,
        string betreff,
        string text,
        IReadOnlyList<string> anmerkungen,
        Func<string, Task>? fortschritt = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Die eigenen Angaben, geholt statt geglaubt.</summary>
/// <remarks>
/// <strong>Der Kontext wird SERVERSEITIG gefüllt.</strong> Ihn vom Browser
/// schicken zu lassen wäre kürzer und würde die Zusage aus ADR-0034 wertlos
/// machen: „nur die eigenen Daten" ist eine Behauptung, die nur der Server
/// halten kann. Geholt wird mit dem Token des Aufrufers, also genau das, was
/// diese Person ohnehin sehen darf.
/// </remarks>
public interface IApplicantLookup
{
    /// <summary>Profile und Werdegang der bewerbenden Person.</summary>
    /// <exception cref="BewerberSchweigt">Ein beteiligter Dienst antwortete nicht.</exception>
    Task<Eigenbild> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Briefkopf — Klarname und PostalAddress. Nie Teil von <see cref="Anschreibenkontext"/>.
/// </summary>
/// <remarks>
/// Eigener Port, damit niemand sie in den Prompt mischt (ADR-0038). Ein
/// Ausfall liefert leer, nicht eine Ausnahme: Senden darf an der PostalAddress
/// nicht scheitern.
/// </remarks>
public interface IKontaktauskunft
{
    /// <summary>Was zum Sendezeitpunkt auf dem Briefkopf steht.</summary>
    Task<ApplicantContact> GetContactAsync(CancellationToken cancellationToken = default);
}

/// <summary>Was die Person über sich selbst geschrieben hat.</summary>
/// <param name="Sprache">
/// In welcher Sprache der Brief entstehen soll — vom Konto, nicht aus einem
/// Header der ResumeRequest (ADR-0031).
/// </param>
public sealed record Eigenbild(
    string Name,
    string Ueberschrift,
    string Text,
    IReadOnlyList<string> Faehigkeiten,
    IReadOnlyList<Werdegangstation> Werdegang,
    string Sprache = "de");

/// <summary>Wie das Unternehmen heißt, an das der Brief geht.</summary>
/// <remarks>
/// Der Name kommt aus dem <em>öffentlichen</em> EmployerProfile — derselbe,
/// den jeder auf der Karriereseite liest. Er ist die PostalAddress des Briefes und
/// keine Aussage über das Unternehmen.
/// </remarks>
public interface IUnternehmensauskunft
{
    /// <summary>Der Name, oder leer, wenn keiner zu bekommen ist.</summary>
    /// <remarks>
    /// Leer statt einer Ausnahme: ein Brief ohne Firmennamen ist ein Brief mit
    /// „Sehr geehrte Damen und Herren", und das ist kein Fehlerfall. Eine
    /// JobApplication daran scheitern zu lassen, dass ein EmployerProfile fehlt,
    /// wäre Strenge zulasten der falschen Person.
    /// </remarks>
    Task<string> NameAsync(
        Noelia.Core.Identity.TenantId company, CancellationToken cancellationToken = default);
}

/// <summary>Ein Dienst, der die eigenen Angaben hält, hat nicht geantwortet.</summary>
public sealed class ApplicantLookupSilent(string reason, Exception? ursache = null)
    : Exception(reason, ursache);
