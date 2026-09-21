using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Wie ein Befehl an einer JobApplication ausgegangen ist.</summary>
/// <remarks>
/// Ein Ergebnis statt geworfener Ausnahmen, damit die Zuordnung zu
/// Statuscodes an <em>einer</em> Job steht: sieben Routen, die jede für sich
/// entscheiden, was ein fremder Case ist, driften auseinander — und die
/// erste, die 403 statt 404 antwortet, verrät, dass es die JobApplication gibt.
/// </remarks>
public abstract record ApplicationResult
{
    private ApplicationResult()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(JobApplication JobApplication) : ApplicationResult;

    /// <summary>Die Job gibt es öffentlich nicht.</summary>
    public sealed record NoJob : ApplicationResult;

    /// <summary>
    /// Gibt es nicht ODER gehört jemand anderem — von außen dasselbe.
    /// </summary>
    /// <remarks>
    /// Die beiden auseinanderzuhalten hieße, auf Zuruf zu bestätigen, dass eine
    /// fremde JobApplication existiert.
    /// </remarks>
    public sealed record Unbekannt : ApplicationResult;

    /// <summary>Die Eingabe ist in Ordnung, der Zustand passt nicht.</summary>
    public sealed record StateConflict(string Grund) : ApplicationResult;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : ApplicationResult;
}
