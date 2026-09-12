namespace WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

/// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
public abstract class Profilfehler(string meldung) : Exception(meldung);

/// <summary>Ein Textfeld ist leer oder zu lang.</summary>
public sealed class Textfehler(string feld, string was) : Profilfehler($"{feld} {was}");

/// <summary>Der Link taugt nicht.</summary>
/// <remarks>
/// Dieselbe Regel wie bei Portfolio-Links (ADR-0021): ein Link wird von fremden
/// Menschen angeklickt, und <c>javascript:</c> in einem Feld, das im Browser
/// landet, ist kein Randfall.
/// </remarks>
public sealed class Linkfehler(string was) : Profilfehler(was);

/// <summary>Es sind zu viele Einträge.</summary>
public sealed class ZuVieleEintraege(string feld, int grenze)
    : Profilfehler($"At most {grenze} {feld} are allowed");
