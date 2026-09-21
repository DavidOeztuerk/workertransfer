namespace WorkerTransfer.Companies.Domain.EmployerProfiles;

/// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
public abstract class ProfileError(string notification) : Exception(notification);

/// <summary>Ein Textfeld ist leer oder zu lang.</summary>
public sealed class Textfehler(string field, string was) : ProfileError($"{field} {was}");

/// <summary>Der Link taugt nicht.</summary>
/// <remarks>
/// Dieselbe Regel wie bei Portfolio-Links (ADR-0021): ein Link wird von fremden
/// Menschen angeklickt, und <c>javascript:</c> in einem Feld, das im Browser
/// landet, ist kein Randfall.
/// </remarks>
public sealed class Linkfehler(string was) : ProfileError(was);

/// <summary>Es sind zu viele Einträge.</summary>
public sealed class ZuVieleEintraege(string field, int limit)
    : ProfileError($"At most {limit} {field} are allowed");
