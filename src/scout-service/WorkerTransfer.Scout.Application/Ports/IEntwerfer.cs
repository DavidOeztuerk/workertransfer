using WorkerTransfer.Scout.Domain.Suchen;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>Der Anbieter hat nicht geantwortet — oder es ist keiner eingerichtet.</summary>
/// <remarks>
/// Ein eigener Fehler und <strong>kein stiller Rückfall auf eine Vorlage</strong>:
/// ein Entwurf, der nicht vom Modell kommt, aber so aussieht, wäre die
/// schlechtere Antwort — jemand hielte ihn für einen Vorschlag und schickte ihn
/// ab. Die Meldung trägt nie den Prompt und nie die Antwort.
/// </remarks>
/// <param name="meldung">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class AnspracheNichtVerfuegbar(string meldung) : Exception(meldung);

/// <summary>
/// Was in den Prompt einer Ansprache darf — und damit auch: was nicht.
/// </summary>
/// <remarks>
/// <para><strong>Diese Klasse IST die Grenze</strong> (ADR-0024, ADR-0036
/// Auflage 4). Sie trägt <em>nur schon sichtbare, genannte Worte</em>: was die
/// angesprochene Person selbst in ihr Profil getippt hat und was dieses
/// Unternehmen ohnehin schon sehen darf, plus die Worte, nach denen das
/// Unternehmen selbst gesucht hat.</para>
///
/// <para>Was hier mit Absicht fehlt: Name, E-Mail-Adresse, <c>SubjectId</c>,
/// Arbeitgeber, Lebenslauf, Marktstatus, Bewerbungen — und der <em>Name des
/// suchenden Unternehmens</em>. Der letzte Punkt ist derselbe wie beim
/// Anzeigenentwurf (ADR-0024): ein Firmenname im Prompt wäre die Einladung,
/// das Modell etwas über den Arbeitgeber sagen zu lassen, was niemand geprüft
/// hat.</para>
///
/// <para><strong>Auch keine Belege.</strong> Sie sind sichtbar, aber nicht
/// genannt: ein GitHub-Topic ist eine Aussage über ein Repository. Es in eine
/// Ansprache zu schreiben, hiesse, es der Person zuzuschreiben — genau der
/// Schritt, den ADR-0033 der Person selbst vorbehält.</para>
///
/// <para>Ein Record mit benannten Feldern und kein freies Wörterbuch, weil ein
/// Wörterbuch beim nächsten Merkmal stillschweigend einen Schlüssel mehr trägt
/// — und niemand es beim Lesen sieht. Ein Feld-Set-Test nagelt die Menge fest.</para>
///
/// <para>Und ein <em>eigener</em> Typ mit <em>eigenen</em> Regeln, kein
/// gemeinsamer Prompt mit einem <c>if</c>: die Regeln für einen Profiltext, für
/// eine Anzeige und für eine Ansprache haben nichts miteinander zu tun, und ein
/// <c>if</c> ist die Stelle, an der eines Tages eine Regel für die falsche Seite
/// gilt.</para>
/// </remarks>
/// <param name="Ueberschrift">Was die Person selbst als Überschrift geschrieben hat.</param>
/// <param name="Genannt">Was sie selbst als Fähigkeiten genannt hat.</param>
/// <param name="Gesucht">Wonach das Unternehmen gesucht hat — seine eigenen Worte.</param>
/// <param name="Wunsch">Was der Mensch am Entwurf anders haben will.</param>
public sealed record Ansprachelage(
    string Ueberschrift,
    IReadOnlyList<string> Genannt,
    IReadOnlyList<string> Gesucht,
    string Wunsch)
{
    /// <summary>Wie lang der Wunsch sein darf.</summary>
    public const int HoechstlaengeWunsch = 300;

    /// <summary>Der Wunsch, geprüft.</summary>
    /// <remarks>
    /// Die Prüfung steht hier und nicht am Endpunkt, weil dieser Typ die Grenze
    /// ist: eine zweite Aufrufstelle bekäme sie sonst nicht mit. Und es ist die
    /// einzige Länge in dieser Klasse, die geprüft werden muss — alles andere
    /// kommt aus gespeicherten, längst begrenzten Feldern.
    /// </remarks>
    public string Wunsch { get; init; } =
        Wunsch is { Length: > HoechstlaengeWunsch }
            ? throw new Eingabefehler(
                $"a wish may not be longer than {HoechstlaengeWunsch} characters")
            : Wunsch;

    /// <summary>Woran das Modell sich zu halten hat.</summary>
    /// <remarks>
    /// <para>Der Kern ist „<em>schreibe eine Frage, keine Beurteilung</em>". Eine
    /// Ansprache ist der erste Satz eines Menschen an einen anderen, den er
    /// nicht kennt; alles, was wie ein Urteil über ihn klingt, ist genau das,
    /// was ADR-0022 fernhält — und die Person könnte es nicht einmal
    /// kommentieren, weil sie den Prompt nie sieht.</para>
    ///
    /// <para>Deshalb auch: keine erfundene Erfahrung, keine Zahl, kein „passt zu
    /// 80 %". Und ausdrücklich die Bitte, <em>nicht</em> so zu tun, als kenne man
    /// die Person.</para>
    /// </remarks>
    public static string Regeln =>
        "Du hilfst einem Menschen in einem Unternehmen, eine erste Nachricht an "
        + "eine Person auf einer Job-Plattform zu formulieren. Schreibe auf "
        + "Deutsch, sachlich, kurz und ohne Werbesprache.\n"
        + "Regeln:\n"
        + "- Erfinde NICHTS hinzu. Benutze nur, was unten steht.\n"
        + "- Beurteile die Person NICHT. Keine Einschaetzung, keine Zahl, kein "
        + "\"passt gut\", keine Behauptung ueber Jahre an Erfahrung.\n"
        + "- Tu nicht so, als kenntest du sie. Schreibe eine Frage, keine "
        + "Feststellung.\n"
        + "- Sag, worum es geht, und ueberlass ihr die Antwort.\n"
        + "- Hoechstens 120 Woerter.\n"
        + "- Gib nur den Text zurueck, ohne Betreff, ohne Unterschrift, ohne "
        + "Erklaerung.";

    /// <summary>Der Prompt, wörtlich — damit ein Test ihn prüfen kann.</summary>
    /// <remarks>
    /// Getrennt vom Versand, weil die interessante Frage nicht ist, ob HTTP
    /// funktioniert, sondern <strong>was hinausgeht</strong>.
    /// </remarks>
    public string Prompt =>
        string.Join('\n', new[]
        {
            Ueberschrift.Length > 0 ? $"Ueberschrift der Person: {Ueberschrift}" : "",
            Genannt.Count > 0 ? $"Von ihr genannte Faehigkeiten: {string.Join(", ", Genannt)}" : "",
            Gesucht.Count > 0 ? $"Gesucht wurde nach: {string.Join(", ", Gesucht)}" : "",
            Wunsch.Length > 0 ? $"Wunsch des Absenders: {Wunsch}" : ""
        }.Where(teil => teil.Length > 0));
}

/// <summary>Schreibt einen Entwurf und behält nichts davon.</summary>
/// <remarks>
/// <para><strong>Ein Entwurf, kein Versand</strong> (ADR-0036 Auflage 4). Dieser
/// Dienst hat keinen Weg, jemandem zu schreiben: kein Postbote, keine
/// Mailadresse, keine Benachrichtigung an die angesprochene Person. Der Text
/// geht an den Browser dessen, der gefragt hat, und liegt dort in einem
/// Formular. Wer ihn abschickt, schickt ihn selbst ab — auf einem Weg, den es
/// hier noch nicht gibt.</para>
///
/// <para>Ein Aufruf, ein Text. Kein Gedächtnis, kein Vektorspeicher, kein
/// Plan-Act-Reflect: alle drei speichern entweder oder handeln ungefragt. Und
/// kein Eintrag im Consent-Ledger — der behauptete eine stehende Erlaubnis, die
/// niemand gegeben hat. <strong>Der Knopf ist die Einwilligung</strong>,
/// informiert und je Benutzung.</para>
/// </remarks>
public interface IEntwerfer
{
    /// <exception cref="AnspracheNichtVerfuegbar">
    /// Kein Anbieter eingerichtet, oder er antwortet nicht.
    /// </exception>
    Task<string> EntwirfAsync(Ansprachelage lage, CancellationToken cancellationToken = default);
}
