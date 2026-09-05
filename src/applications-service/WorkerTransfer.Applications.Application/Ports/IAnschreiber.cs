namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Es ist kein Anbieter eingerichtet — oder er antwortet nicht.</summary>
public sealed class AnschreibenNichtVerfuegbar(string meldung) : Exception(meldung);

/// <summary>Eine Station aus dem eigenen Lebenslauf, wie das Modell sie sieht.</summary>
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
/// <strong>Nie eine dritte Person.</strong> Keine anderen Bewerber, keine
/// Vergleichszahlen, keine Einschätzung — nichts, was eine Aussage
/// <em>über</em> jemanden wäre. Ein Test hält die Feldliste fest, so wie
/// ADR-0024 es für die anderen beiden Verbraucher tut.
/// </para>
/// <para>
/// <strong>Der Firmenname darf hier stehen</strong>, anders als bei
/// <c>Anzeigenentwurf</c>. Dort wäre er der Anfang einer Aussage über ein
/// Unternehmen; hier ist er die Anschrift. Man kann keinen Brief schreiben,
/// ohne zu wissen, an wen.
/// </para>
/// </remarks>
public sealed record Anschreibenkontext(
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
    /// der Stelle, an der sie am leichtesten zu übersehen ist: „ich bin zu 90 %
    /// geeignet" wäre eine Zahl über einen Menschen, nur aus seiner eigenen
    /// Feder.
    /// </remarks>
    public const string Regeln = """
        Du schreibst ein Bewerbungsanschreiben — in der Ich-Form, aus der Sicht
        der Person, die sich bewirbt.

        1. Verwende ausschliesslich die angegebenen Angaben der Person. Erfinde
           KEINE Qualifikationen, keine Arbeitgeber, keine Zeitraeume, keine
           Abschluesse und keine Zahlen. Was nicht dasteht, kommt nicht vor.
        2. Beziehe dich konkret auf die Stellenbeschreibung und belege das mit
           Angaben aus dem Werdegang der Person.
        3. Keine Selbstbewertung in Zahlen — kein Prozentwert, keine Note, kein
           "zu 90 % geeignet".
        4. Keine Floskeln wie "hiermit bewerbe ich mich".
        5. Hoechstens eine Seite, etwa 250 bis 350 Woerter Haupttext.
        6. Antworte in der angegebenen Sprache.

        Ausgabeformat, genau so:
        BETREFF: <Betreffzeile>
        ---
        <Anschreiben mit Anrede und Gruss, ohne Briefkopf>
        """;
}

/// <summary>Entwirft ein Anschreiben — auf Bitte, und nur dann.</summary>
public interface IAnschreiber
{
    /// <summary>Ob überhaupt ein Anbieter eingerichtet ist.</summary>
    /// <remarks>
    /// Wird gefragt, BEVOR ein Entwurf entsteht: ein leerer Entwurf im Zustand
    /// „Pruefen" wäre eine Einladung, ihn versehentlich freizugeben (ADR-0034).
    /// </remarks>
    bool Eingerichtet { get; }

    /// <summary>Schreibt. Behält nichts.</summary>
    /// <exception cref="AnschreibenNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    Task<string> SchreibeAsync(
        Anschreibenkontext kontext, CancellationToken cancellationToken = default);

    /// <summary>Überarbeitet anhand der Anmerkungen.</summary>
    /// <remarks>
    /// Die Anmerkungen sind der ganze Auftrag — wörtlich, samt Zitat, und mit
    /// der Anweisung, alles Unkommentierte zu lassen.
    /// </remarks>
    /// <exception cref="AnschreibenNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    Task<string> UeberarbeiteAsync(
        Anschreibenkontext kontext,
        string betreff,
        string text,
        IReadOnlyList<string> anmerkungen,
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
public interface IBewerberauskunft
{
    /// <summary>Profil und Werdegang der bewerbenden Person.</summary>
    /// <exception cref="BewerberSchweigt">Ein beteiligter Dienst antwortete nicht.</exception>
    Task<Eigenbild> HoleAsync(CancellationToken cancellationToken = default);
}

/// <summary>Was die Person über sich selbst geschrieben hat.</summary>
/// <param name="Sprache">
/// In welcher Sprache der Brief entstehen soll — vom Konto, nicht aus einem
/// Kopf der Anfrage (ADR-0031).
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
/// Der Name kommt aus dem <em>öffentlichen</em> Arbeitgeberprofil — derselbe,
/// den jeder auf der Karriereseite liest. Er ist die Anschrift des Briefes und
/// keine Aussage über das Unternehmen.
/// </remarks>
public interface IUnternehmensauskunft
{
    /// <summary>Der Name, oder leer, wenn keiner zu bekommen ist.</summary>
    /// <remarks>
    /// Leer statt einer Ausnahme: ein Brief ohne Firmennamen ist ein Brief mit
    /// „Sehr geehrte Damen und Herren", und das ist kein Fehlerfall. Eine
    /// Bewerbung daran scheitern zu lassen, dass ein Arbeitgeberprofil fehlt,
    /// wäre Strenge zulasten der falschen Person.
    /// </remarks>
    Task<string> NameAsync(
        Girder.Core.Identity.TenantId firma, CancellationToken cancellationToken = default);
}

/// <summary>Ein Dienst, der die eigenen Angaben hält, hat nicht geantwortet.</summary>
public sealed class BewerberSchweigt(string grund, Exception? ursache = null)
    : Exception(grund, ursache);
