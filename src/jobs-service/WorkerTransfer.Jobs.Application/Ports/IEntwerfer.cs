using WorkerTransfer.Jobs.Domain.Stellen;

namespace WorkerTransfer.Jobs.Application.Ports;

/// <summary>Der Anbieter hat nicht geantwortet — oder es ist keiner eingerichtet.</summary>
public sealed class EntwurfNichtVerfuegbar(string meldung) : Exception(meldung);

/// <summary>
/// Was in den Prompt darf — und damit auch: was nicht (ADR-0024).
/// </summary>
/// <param name="Titel">Was das Unternehmen selbst geschrieben hat.</param>
/// <param name="Beschreibung">Ebenso.</param>
/// <param name="Faehigkeiten">Was es selbst verlangt.</param>
/// <param name="Ort">Wo.</param>
/// <param name="Wunsch">Was es geändert haben will („kürzer", „konkreter").</param>
/// <remarks>
/// <b>Diese Klasse IST die Grenze.</b> Sie trägt, was das Unternehmen über
/// seine eigene Anzeige geschrieben hat, und sonst nichts: <b>keine
/// <c>tenant_id</c>, keinen Firmennamen</b> — und schon gar nichts über einen
/// Menschen.
/// <para>
/// Das ist der Grund, warum dieser Entwurf überhaupt gebaut werden darf: er
/// hilft einem Unternehmen, <em>seine eigene</em> Anzeige zu formulieren, und
/// sagt nie etwas <em>über</em> jemanden. Scout, Kandidatenbewertung,
/// Gehaltsempfehlung und Team-Analyse zielen alle auf Menschen und sind
/// deshalb nicht hier.
/// </para>
/// <para>
/// Eine eigene Kontextklasse und kein geteilter Prompt mit Verzweigung: die
/// Regeln für einen Profiltext und die für eine Stellenanzeige haben nichts
/// miteinander zu tun, und eine Verzweigung ist genau die Stelle, an der
/// irgendwann „erfinde nichts über die Person" für eine Anzeige gälte — oder,
/// schlimmer, umgekehrt.
/// </para>
/// </remarks>
public sealed record Anzeigenentwurf(
    string Titel,
    string Beschreibung,
    IReadOnlyList<string> Faehigkeiten,
    string Ort,
    string Wunsch)
{
    /// <summary>Der Wunsch, geprüft.</summary>
    /// <remarks>
    /// Die Prüfung steht hier und nicht am Endpunkt, weil dieser Typ die Grenze
    /// ist — eine zweite Aufrufstelle bekäme sie sonst nicht mit. Titel,
    /// Beschreibung, Ort und Fähigkeiten sind bereits Wertobjekte der Domäne und
    /// dort begrenzt; der Wunsch kommt roh aus dem Rumpf der Anfrage.
    /// </remarks>
    public string Wunsch { get; init; } =
        Wunsch is { Length: > Stelle.HoechstlaengeWunsch }
            ? throw new WunschFehler()
            : Wunsch;

    /// <summary>Woran das Modell sich zu halten hat.</summary>
    public static string Regeln =>
        "Du hilfst einem Unternehmen, seine eigene Stellenanzeige zu "
        + "formulieren. Schreibe auf Deutsch, sachlich und ohne Werbesprache.\n"
        + "Regeln:\n"
        + "- Erfinde NICHTS hinzu. Benutze nur, was unten steht.\n"
        + "- Keine Aussagen über Bewerberinnen oder Bewerber, keine "
        + "Anforderungen an Persönlichkeit, kein Alter, kein Geschlecht.\n"
        + "- Keine Superlative und keine Behauptungen über das Unternehmen, die "
        + "nicht unten stehen.\n"
        + "- Höchstens 250 Wörter.\n"
        + "- Gib nur den Text zurück, ohne Anrede und ohne Erklärung.";

    /// <summary>Der Prompt, wörtlich — damit ein Test ihn prüfen kann.</summary>
    public string Prompt =>
        string.Join('\n', new[]
        {
            Titel.Length > 0 ? $"Titel: {Titel}" : "",
            Beschreibung.Length > 0 ? $"Bisheriger Text: {Beschreibung}" : "",
            Faehigkeiten.Count > 0 ? $"Anforderungen: {string.Join(", ", Faehigkeiten)}" : "",
            Ort.Length > 0 ? $"Ort: {Ort}" : "",
            Wunsch.Length > 0 ? $"Wunsch: {Wunsch}" : ""
        }.Where(teil => teil.Length > 0));
}

/// <summary>Schreibt einen Entwurf und behält nichts davon.</summary>
public interface IEntwerfer
{
    /// <exception cref="EntwurfNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    Task<string> EntwirfAsync(
        Anzeigenentwurf entwurf, CancellationToken cancellationToken = default);
}
