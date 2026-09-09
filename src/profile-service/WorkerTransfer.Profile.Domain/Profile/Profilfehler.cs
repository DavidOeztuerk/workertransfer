namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>Die Überschrift fehlt oder ist zu lang.</summary>
/// <param name="grund">Was an ihr nicht stimmt.</param>
public sealed class UeberschriftFehler(string grund) : Eingabefehler($"Die Überschrift {grund}.");

/// <summary>Der Freitext ist zu lang.</summary>
public sealed class TextFehler()
    : Eingabefehler($"Der Text darf höchstens {Profil.HoechstlaengeText} Zeichen haben.");

/// <summary>Der Ort ist zu lang.</summary>
public sealed class OrtFehler()
    : Eingabefehler($"Der Ort darf höchstens {Profil.HoechstlaengeOrt} Zeichen haben.");

/// <summary>Der Wunsch an die Formulierungshilfe ist zu lang.</summary>
/// <remarks>
/// Er ist eine Anweisung („kürzer“, „sachlicher“), kein Inhalt — der Inhalt
/// steht im Profil und ist dort längst begrenzt. Ungebremst wäre dieses Feld
/// der einzige Weg, über den ein Aufrufer beliebig viel Text an den fremden
/// Anbieter schickt: es kommt aus dem Rumpf der Anfrage und geht an keinem
/// Wertobjekt vorbei.
/// </remarks>
public sealed class WunschFehler()
    : Eingabefehler(
        $"Der Wunsch darf höchstens {Profil.HoechstlaengeWunsch} Zeichen haben.");
