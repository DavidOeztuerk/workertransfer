using Girder.Core.Identity;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>Wonach eine Seite eingeschränkt wird.</summary>
/// <param name="Anzahl">Wie viele Zeilen höchstens.</param>
/// <param name="Ab">Wo es weitergeht, oder <c>null</c> für den Anfang.</param>
/// <param name="Faehigkeiten">Die gesuchten Worte, kanonisch.</param>
/// <param name="Ort">Teiltext, ohne Rücksicht auf Groß-/Kleinschreibung.</param>
/// <param name="NurRemote">Nur, wer Remote ausdrücklich angekreuzt hat.</param>
/// <remarks>
/// <para><strong>Die Worte sind ein ODER, und es gibt kein UND mehr.</strong>
/// <c>GET /candidates</c> suchte mit UND und ist am 11.09.2026 gefallen; die
/// Wahl zwischen beidem fiel mit ihm. Ein Zweig ohne Aufrufer ist kein Vorrat,
/// sondern das, was später falsch wiederbelebt wird.</para>
///
/// <para>Das ODER ist auch keine Lockerung, sondern die Voraussetzung für die
/// Häkchenliste des Scouts: unter UND erfüllt jeder Treffer alle Bedingungen,
/// jedes Häkchen wäre gesetzt, und „welche Fähigkeit fehlt" hätte keine
/// Antwort (ADR-0036 Entscheidung 2). Auch die erste Auflage wäre dann leer —
/// es gäbe nichts, wonach man sortieren könnte.</para>
///
/// <para>Sichtbar wird dadurch nichts, was ohne den Filter verborgen wäre: die
/// Einwilligung wird danach geprüft, nicht hier — der Speicher kennt keine
/// Sichtbarkeit (ADR-0020).</para>
/// </remarks>
public sealed record Seitenanfrage(
    int Anzahl,
    Seitenzeiger? Ab = null,
    IReadOnlyList<string>? Faehigkeiten = null,
    string Ort = "",
    bool NurRemote = false);

/// <summary>Eine Seite Profile.</summary>
/// <param name="Eintraege">Die Zeilen, zuletzt geänderte zuerst.</param>
/// <param name="Weiter">Wo es weitergeht, oder <c>null</c> am Ende.</param>
public sealed record Profilseite(IReadOnlyList<Profil> Eintraege, Seitenzeiger? Weiter);

/// <summary>Wo die Profile liegen.</summary>
public interface IProfilspeicher
{
    /// <summary>Das Profil einer Person, oder <c>null</c>.</summary>
    Task<Profil?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder ändert — ein Profil je Person.</summary>
    /// <remarks>
    /// Muss aufgerufen werden. Das Aggregat kommt losgelöst aus
    /// <see cref="HoleAsync"/>, also bleibt jede Änderung ohne diesen Aufruf im
    /// Arbeitsspeicher: im Test folgenlos, in Betrieb ein verlorener Schreibgang.
    /// </remarks>
    Task SpeichereAsync(Profil profil, CancellationToken cancellationToken = default);

    /// <summary>Eine Seite, nach <see cref="Seitenanfrage"/>.</summary>
    Task<Profilseite> SeiteAsync(
        Seitenanfrage anfrage, CancellationToken cancellationToken = default);

    /// <summary>Löscht das Profil einer Person und sagt, was stehen blieb.</summary>
    /// <remarks>
    /// Immer 0: dieser Dienst kennt keinen Aufbewahrungsfall. Der Rückgabewert
    /// ist trotzdem da, weil jeder Empfänger dieselbe Quittung gibt — der
    /// Ursprung soll erfahren, was blieb, statt es zu vermuten (ADR-0027 §3.4).
    /// <para>
    /// Auf eine Zeile, die schon weg ist, ist das von Natur aus wirkungslos.
    /// Das ist keine Nachlässigkeit, sondern die Voraussetzung dafür, dass die
    /// Zustellung „mindestens einmal“ sein darf.
    /// </para>
    /// </remarks>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
