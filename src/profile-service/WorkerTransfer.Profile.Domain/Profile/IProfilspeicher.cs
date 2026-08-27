using Girder.Core.Identity;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>Wonach eine Seite eingeschränkt wird.</summary>
/// <param name="Anzahl">Wie viele Zeilen höchstens.</param>
/// <param name="Ab">Wo es weitergeht, oder <c>null</c> für den Anfang.</param>
/// <param name="Faehigkeiten">Alle davon muss jemand nennen — UND, nicht ODER.</param>
/// <param name="Ort">Teiltext, ohne Rücksicht auf Groß-/Kleinschreibung.</param>
/// <param name="NurRemote">Nur, wer Remote ausdrücklich angekreuzt hat.</param>
/// <remarks>
/// Die Filter verengen eine Menge, die es schon gibt: sichtbar wird dadurch
/// nichts, was ohne sie verborgen wäre. Die Einwilligung wird danach geprüft,
/// nicht hier — der Speicher kennt keine Sichtbarkeit (ADR-0020).
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
