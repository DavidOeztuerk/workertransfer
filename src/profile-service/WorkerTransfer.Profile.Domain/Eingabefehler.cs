namespace WorkerTransfer.Profile.Domain;

/// <summary>Was eine Person eingetragen hat, passt nicht in ein Profil.</summary>
/// <remarks>
/// Eine gemeinsame Wurzel, damit die Endpunkte <em>eine</em> Stelle haben, an
/// der aus „so nicht“ ein 422 wird. Ohne sie fängt jeder Endpunkt drei bis vier
/// Typen einzeln, und der nächste hinzukommende Fehler wird an einer der
/// Stellen vergessen — und kommt dort als 500 heraus.
/// <para>
/// Die Meldungen tragen nie, was jemand geschrieben hat: sie nennen die Regel
/// („höchstens 120 Zeichen“), nicht den Text. Eine Fehlermeldung landet in
/// Protokollen und Bildschirmfotos.
/// </para>
/// </remarks>
/// <param name="meldung">Welche Regel verletzt ist.</param>
public abstract class Eingabefehler(string meldung) : Exception(meldung);
