namespace WorkerTransfer.Jobs.Domain.Stellen;

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
public abstract class Eingabefehler(string meldung) : Exception(meldung);

/// <summary>Ein Textfeld ist leer oder zu lang.</summary>
public sealed class TextFehler(string feld, string grund) : Eingabefehler($"{feld} {grund}.");

/// <summary>Zu viele Anforderungen oder eine zu lange.</summary>
public sealed class Faehigkeitsfehler(string grund) : Eingabefehler(grund);

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(Stellenstand jetzt, Stellenstand gewollt)
    : Eingabefehler($"Eine {jetzt}-Stelle kann nicht {gewollt} werden.");

/// <summary>Der Wunsch an die Formulierungshilfe ist zu lang.</summary>
/// <remarks>
/// Ungebremst wäre dieses Feld der einzige Weg, über den ein Aufrufer beliebig
/// viel Text an den fremden Anbieter schickt: es kommt aus dem Rumpf der Anfrage
/// und geht an keinem Wertobjekt vorbei.
/// </remarks>
public sealed class WunschFehler()
    : Eingabefehler($"Der Wunsch darf höchstens {Stelle.HoechstlaengeWunsch} Zeichen haben.");
