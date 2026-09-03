using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Welcher Anbieter für die Entwurfshilfe gefragt wird.</summary>
/// <remarks>
/// <strong>Zwei Werte für viele Anbieter.</strong> Ollama, OpenAI, vLLM,
/// LM Studio und Mistral sprechen alle dieselbe Gestalt
/// (<c>/v1/chat/completions</c>); Anthropic spricht eine eigene. Deshalb steht
/// hier nicht eine Liste von Firmennamen, sondern die Frage, die den Code
/// unterscheidet — eine Liste von Firmen wäre bei jedem neuen Anbieter eine
/// Migration.
/// <para>
/// <strong><see cref="Keiner"/> ist die Vorgabe und keine Verlegenheit.</strong>
/// Wer nichts einstellt, fragt niemanden, und die Oberfläche sagt das. Eine
/// Voreinstellung auf einen Fremdanbieter wäre eine Einwilligung, die niemand
/// gegeben hat.
/// </para>
/// </remarks>
public enum KiAnbieter
{
    /// <summary>Keiner. Es wird nichts nach draussen gegeben.</summary>
    Keiner,

    /// <summary>Alles, was <c>/v1/chat/completions</c> spricht — Ollama eingeschlossen.</summary>
    OpenAiKompatibel,

    /// <summary>Anthropics eigene Gestalt.</summary>
    Anthropic
}

/// <summary>
/// Was eine Person über sich und über die Verarbeitung ihrer Daten entschieden hat.
/// </summary>
/// <remarks>
/// <strong>Alles hier ist eine Entscheidung, keine Ableitung.</strong> Kein Wert
/// wird aus dem Verhalten geschlossen, keiner aus der Zugehörigkeit zu einem
/// Unternehmen. Was nicht gesetzt wurde, steht auf der zurückhaltendsten
/// Stellung — nicht auf der bequemsten.
/// <para>
/// <strong>Der Schlüssel liegt verschlüsselt und kommt nie zurück.</strong> Die
/// Oberfläche erfährt, OB einer hinterlegt ist und wie seine letzten vier
/// Zeichen lauten; mehr braucht niemand, um ihn wiederzuerkennen, und mehr
/// darf über die Leitung nicht zurückgehen. Ein Geheimnis, das man abrufen
/// kann, ist eines, das man abziehen kann.
/// </para>
/// </remarks>
public sealed class Kontoeinstellungen
{
    private Kontoeinstellungen(SubjectId wer)
    {
        Wer = wer;
    }

    /// <summary>Wessen Einstellungen.</summary>
    public SubjectId Wer { get; }

    // ---------------------------------------------------------------- Daten

    /// <summary>
    /// Nach wie vielen Monaten ohne Anmeldung das Konto von selbst gelöscht wird.
    /// </summary>
    /// <remarks>
    /// <c>null</c> heisst „nie von selbst". Das ist die Vorgabe, weil eine
    /// automatische Löschung eine Entscheidung über fremde Daten ist, die
    /// niemand getroffen hat — und weil ein Konto, das im Urlaub verfällt, das
    /// Gegenteil von Kontrolle ist.
    /// </remarks>
    public int? LoeschungNachMonaten { get; private set; }

    // ------------------------------------------------------------------- KI

    /// <summary>Welcher Anbieter gefragt werden darf.</summary>
    public KiAnbieter Anbieter { get; private set; } = KiAnbieter.Keiner;

    /// <summary>Die Adresse des Anbieters. Bei Ollama die eigene Maschine.</summary>
    public string Adresse { get; private set; } = string.Empty;

    /// <summary>Welches Modell.</summary>
    public string Modell { get; private set; } = string.Empty;

    /// <summary>Der Schlüssel, verschlüsselt. Leer heisst: keiner hinterlegt.</summary>
    /// <remarks>
    /// Verlässt diesen Dienst nie im Klartext und geht nie an den Browser
    /// zurück. Bei einem lokalen Ollama bleibt er leer — dort gibt es keinen.
    /// </remarks>
    public string SchluesselVerschluesselt { get; private set; } = string.Empty;

    /// <summary>Die letzten vier Zeichen, zum Wiedererkennen.</summary>
    public string SchluesselEndung { get; private set; } = string.Empty;

    /// <summary>
    /// Wird festgehalten, DASS eine Anfrage hinausging — nie, was darin stand.
    /// </summary>
    /// <remarks>
    /// Der EU AI Act verlangt von Betreibern eines Systems im Beschäftigungs-
    /// kontext ein Protokoll über die Tätigkeit des Systems. Diese Plattform
    /// bewertet niemanden und fällt damit nicht unter die Pflichten für
    /// Hochrisikosysteme (ADR-0022, ADR-0024) — wer das Protokoll trotzdem
    /// will, bekommt es. Es hält Zeitpunkt und Art fest, nie den Text: sonst
    /// entstünde beim Nachweisen genau die Sammlung, die der Entwurf vermeidet.
    /// </remarks>
    public bool KiProtokoll { get; private set; }

    // --------------------------------------------------------------- Ansicht

    /// <summary>Die Einstellungen, wie eine Zeile sie hält.</summary>
    /// <param name="wer">Wessen.</param>
    /// <param name="loeschungNachMonaten">Verfall, oder <c>null</c>.</param>
    /// <param name="anbieter">Welcher KI-Anbieter.</param>
    /// <param name="adresse">Dessen Adresse.</param>
    /// <param name="modell">Dessen Modell.</param>
    /// <param name="schluesselVerschluesselt">Der Schlüssel, verschlüsselt.</param>
    /// <param name="schluesselEndung">Die letzten vier Zeichen.</param>
    /// <param name="kiProtokoll">Ob Anfragen protokolliert werden.</param>
    /// <returns>Die Einstellungen.</returns>
    public static Kontoeinstellungen Wiederherstellen(
        SubjectId wer,
        int? loeschungNachMonaten,
        KiAnbieter anbieter,
        string adresse,
        string modell,
        string schluesselVerschluesselt,
        string schluesselEndung,
        bool kiProtokoll) =>
        new(wer)
        {
            LoeschungNachMonaten = loeschungNachMonaten,
            Anbieter = anbieter,
            Adresse = adresse,
            Modell = modell,
            SchluesselVerschluesselt = schluesselVerschluesselt,
            SchluesselEndung = schluesselEndung,
            KiProtokoll = kiProtokoll
        };

    /// <summary>Die zurückhaltendste Stellung — für ein Konto, das nie etwas gesetzt hat.</summary>
    /// <param name="wer">Wessen.</param>
    /// <returns>Einstellungen ohne Anbieter, ohne Verfall, ohne Protokoll.</returns>
    public static Kontoeinstellungen Vorgabe(SubjectId wer) => new(wer);

    /// <summary>Setzt die Datenschutz-Entscheidungen.</summary>
    /// <param name="loeschungNachMonaten">
    /// Verfall in Monaten, oder <c>null</c> für „nie von selbst". Werte unter
    /// drei Monaten werden abgelehnt: ein Konto, das nach vier Wochen
    /// verschwindet, verliert jemand im Urlaub.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Der Verfall ist zu kurz.</exception>
    public void SetzeDatenschutz(int? loeschungNachMonaten)
    {
        if (loeschungNachMonaten is { } monate && monate is < 3 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(loeschungNachMonaten), monate,
                "Ein Verfall liegt zwischen 3 und 120 Monaten.");
        }

        LoeschungNachMonaten = loeschungNachMonaten;
    }

    /// <summary>Setzt den KI-Anbieter. Der Schlüssel wird davon nicht angefasst.</summary>
    /// <remarks>
    /// Getrennt vom Schlüssel, weil es zwei Vorgänge sind: den Anbieter wechselt
    /// man oft, den Schlüssel selten. Wer beides in einem Aufruf setzte, müsste
    /// den Schlüssel jedes Mal mitschicken — und ein Geheimnis, das bei jedem
    /// Speichern über die Leitung geht, geht öfter über die Leitung, als es muss.
    /// </remarks>
    /// <param name="anbieter">Welcher.</param>
    /// <param name="adresse">Dessen Adresse.</param>
    /// <param name="modell">Dessen Modell.</param>
    /// <param name="kiProtokoll">Ob Anfragen protokolliert werden.</param>
    public void SetzeKi(KiAnbieter anbieter, string adresse, string modell, bool kiProtokoll)
    {
        Anbieter = anbieter;
        Adresse = (adresse ?? string.Empty).Trim();
        Modell = (modell ?? string.Empty).Trim();
        KiProtokoll = kiProtokoll;

        // Kein Anbieter heisst kein Schlüssel. Ihn stehen zu lassen wäre ein
        // Geheimnis ohne Zweck — und das schlechteste, was man aufbewahren kann.
        if (anbieter == KiAnbieter.Keiner)
        {
            SchluesselVerschluesselt = string.Empty;
            SchluesselEndung = string.Empty;
        }
    }

    /// <summary>Hinterlegt einen Schlüssel.</summary>
    /// <param name="verschluesselt">Der Schlüssel, bereits verschlüsselt.</param>
    /// <param name="endung">Seine letzten vier Zeichen.</param>
    public void SetzeSchluessel(string verschluesselt, string endung)
    {
        SchluesselVerschluesselt = verschluesselt ?? string.Empty;
        SchluesselEndung = endung ?? string.Empty;
    }

    /// <summary>Entfernt den Schlüssel.</summary>
    public void LoescheSchluessel()
    {
        SchluesselVerschluesselt = string.Empty;
        SchluesselEndung = string.Empty;
    }
}
