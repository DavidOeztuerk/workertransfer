namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
public abstract class Eingabefehler(string notification) : Exception(notification);

/// <summary>Ein Textfeld ist leer oder zu lang.</summary>
public sealed class TextFehler(string field, string reason) : Eingabefehler($"{field} {reason}.");

/// <summary>Zu viele Anforderungen oder eine zu lange.</summary>
public sealed class SkillError(string reason) : Eingabefehler(reason);

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(JobState now, JobState gewollt)
    : Eingabefehler($"Eine {now}-Stelle kann nicht {gewollt} werden.");

/// <summary>Der Wunsch an die Formulierungshilfe ist zu lang.</summary>
/// <remarks>
/// Ungebremst wäre dieses Feld der einzige Weg, über den ein Aufrufer beliebig
/// viel Text an den fremden Anbieter schickt: es kommt aus dem Rumpf der ResumeRequest
/// und geht an keinem Wertobjekt vorbei.
/// </remarks>
public sealed class PreferenceError()
    : Eingabefehler($"Der Wunsch darf höchstens {Job.HoechstlaengeWunsch} Zeichen haben.");
