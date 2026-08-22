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
