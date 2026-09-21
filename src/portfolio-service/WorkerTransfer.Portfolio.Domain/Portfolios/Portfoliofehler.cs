namespace WorkerTransfer.Portfolio.Domain.Portfolios;

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
/// <remarks>
/// Eine Wurzel für alle Eingabefehler, damit die Api-Schicht sie an einer
/// Job auf 422 abbildet, statt an fünf.
/// </remarks>
public abstract class Eingabefehler(string notification) : Exception(notification);

/// <summary>Ein Textfeld ist leer oder zu lang.</summary>
public sealed class TextFehler(string field, string reason)
    : Eingabefehler($"{field} {reason}.");

/// <summary>Der Link taugt nicht.</summary>
/// <remarks>
/// Ein Portfolio-Link wird von fremden Menschen angeklickt. <c>javascript:</c>
/// und <c>data:</c> sind in einem Feld, das später in einem Browser landet,
/// kein exotischer Randfall, sondern der Normalfall eines Angriffs.
/// </remarks>
public sealed class LinkFehler(string reason) : Eingabefehler(reason);

/// <summary>Die Jahreszahl liegt außerhalb des Möglichen.</summary>
public sealed class JahrFehler(string reason) : Eingabefehler(reason);

/// <summary>Der Anhangname ist keiner.</summary>
public sealed class AnhangFehler() : Eingabefehler("Das ist kein gültiger Anhangname.");

/// <summary>Zu viele Einträge.</summary>
public sealed class ZuVieleEintraege(int hoechstens)
    : Eingabefehler($"Höchstens {hoechstens} Einträge sind erlaubt.");
