namespace WorkerTransfer.Profile.Application.Ports;

/// <summary>Der Anbieter hat nicht geantwortet — oder es ist keiner eingerichtet.</summary>
/// <remarks>
/// Ein eigener Fehler und <b>kein stiller Rückfall auf eine Vorlage</b>: ein
/// Entwurf, der nicht vom Modell kommt, aber so aussieht, wäre die schlechtere
/// Antwort. Die Meldung trägt nie den Prompt und nie die Antwort.
/// </remarks>
/// <param name="meldung">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class EntwurfNichtVerfuegbar(string meldung) : Exception(meldung);

/// <summary>
/// Was in den Prompt darf — und damit auch: was nicht (ADR-0024).
/// </summary>
/// <param name="Ueberschrift">Was die Person selbst als Überschrift geschrieben hat.</param>
/// <param name="Text">Was sie selbst über sich geschrieben hat.</param>
/// <param name="Faehigkeiten">Was sie selbst genannt hat.</param>
/// <param name="Wunsch">Was sie geändert haben will („kürzer“, „sachlicher“).</param>
/// <remarks>
/// <b>Diese Klasse IST die Grenze.</b> Sie trägt, was die Person über sich
/// geschrieben hat, und sonst nichts: kein Name, keine E-Mail-Adresse, keine
/// <c>SubjectId</c>, kein Arbeitgeber, kein Lebenslauf, keine Bewerbung, kein
/// Marktstatus.
/// <para>
/// Ein Record mit benannten Feldern und kein freies Wörterbuch, weil ein
/// Wörterbuch beim nächsten Merkmal stillschweigend einen Schlüssel mehr trägt
/// — und niemand es beim Lesen sieht.
/// </para>
/// </remarks>
public sealed record Entwurfslage(
    string Ueberschrift,
    string Text,
    IReadOnlyList<string> Faehigkeiten,
    string Wunsch)
{
    /// <summary>Woran das Modell sich zu halten hat.</summary>
    /// <remarks>
    /// „Schreibe ALS die Person“ ist der Kern: der Entwurf ist ein Vorschlag für
    /// ihren eigenen Text, keine Beschreibung von außen. Daher auch das Verbot,
    /// etwas hinzuzuerfinden — eine Fähigkeit im Profil, die die Person nie
    /// genannt hat, ist eine Falschaussage über sie, und sie merkt es womöglich
    /// erst im Gespräch.
    /// <para>
    /// Eine eigene Regelmenge je Art von Entwurf, statt eines gemeinsamen
    /// Prompts mit Verzweigung: die Regeln für einen Profiltext und die für eine
    /// Stellenanzeige haben nichts miteinander zu tun, und eine Verzweigung ist
    /// die Stelle, an der irgendwann eine Regel für die falsche Seite gilt.
    /// </para>
    /// </remarks>
    public static string Regeln =>
        "Du hilfst einer Person, ihren eigenen Profiltext auf einer "
        + "Job-Plattform zu formulieren. Schreibe in der Ich-Form, auf Deutsch, "
        + "sachlich und ohne Werbesprache.\n"
        + "Regeln:\n"
        + "- Erfinde NICHTS hinzu. Benutze nur, was unten steht. Wenn dort wenig "
        + "steht, schreibe wenig.\n"
        + "- Keine Superlative, keine Behauptungen über Erfahrung in Jahren, "
        + "keine Bewertung der Person.\n"
        + "- Höchstens 120 Wörter.\n"
        + "- Gib nur den Text zurück, ohne Anrede, ohne Überschrift, ohne "
        + "Erklärung.";

    /// <summary>Der Prompt, wörtlich — damit ein Test ihn prüfen kann.</summary>
    /// <remarks>
    /// Getrennt vom Versand, weil die interessante Frage nicht ist, ob HTTP
    /// funktioniert, sondern <b>was hinausgeht</b>.
    /// </remarks>
    public string Prompt =>
        string.Join('\n', new[]
        {
            Ueberschrift.Length > 0 ? $"Bisherige Überschrift: {Ueberschrift}" : "",
            Text.Length > 0 ? $"Bisheriger Text: {Text}" : "",
            Faehigkeiten.Count > 0 ? $"Fähigkeiten: {string.Join(", ", Faehigkeiten)}" : "",
            Wunsch.Length > 0 ? $"Wunsch der Person: {Wunsch}" : ""
        }.Where(teil => teil.Length > 0));
}

/// <summary>Schreibt einen Entwurf und behält nichts davon.</summary>
/// <remarks>
/// Ein Aufruf, ein Text. Kein Gedächtnis, kein Vektorspeicher, kein
/// Plan-Act-Reflect: alle drei speichern entweder oder handeln ungefragt. Und
/// kein Eintrag im Consent-Ledger — der behauptete eine stehende Erlaubnis, die
/// niemand gegeben hat. <b>Der Knopf ist die Einwilligung</b>, informiert und
/// je Benutzung.
/// </remarks>
public interface IEntwerfer
{
    /// <exception cref="EntwurfNichtVerfuegbar">
    /// Kein Anbieter eingerichtet, oder er antwortet nicht.
    /// </exception>
    Task<string> EntwirfAsync(Entwurfslage lage, CancellationToken cancellationToken = default);
}
