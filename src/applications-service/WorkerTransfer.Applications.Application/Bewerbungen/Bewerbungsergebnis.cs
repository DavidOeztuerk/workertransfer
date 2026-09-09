using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Application.Bewerbungen;

/// <summary>Wie ein Befehl an einer Bewerbung ausgegangen ist.</summary>
/// <remarks>
/// Ein Ergebnis statt geworfener Ausnahmen, damit die Zuordnung zu
/// Statuscodes an <em>einer</em> Stelle steht: sieben Routen, die jede für sich
/// entscheiden, was ein fremder Vorgang ist, driften auseinander — und die
/// erste, die 403 statt 404 antwortet, verrät, dass es die Bewerbung gibt.
/// </remarks>
public abstract record Bewerbungsergebnis
{
    private Bewerbungsergebnis()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(Bewerbung Bewerbung) : Bewerbungsergebnis;

    /// <summary>Die Stelle gibt es öffentlich nicht.</summary>
    public sealed record KeineStelle : Bewerbungsergebnis;

    /// <summary>
    /// Gibt es nicht ODER gehört jemand anderem — von außen dasselbe.
    /// </summary>
    /// <remarks>
    /// Die beiden auseinanderzuhalten hieße, auf Zuruf zu bestätigen, dass eine
    /// fremde Bewerbung existiert.
    /// </remarks>
    public sealed record Unbekannt : Bewerbungsergebnis;

    /// <summary>Die Eingabe ist in Ordnung, der Zustand passt nicht.</summary>
    public sealed record Zustandskonflikt(string Grund) : Bewerbungsergebnis;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : Bewerbungsergebnis;
}
