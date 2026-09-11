namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>Die Worte, mit denen eine Pendelstufe auf dem Draht steht.</summary>
/// <remarks>
/// <para>Die Umrechnung steht in der Domäne und nicht am Endpunkt, weil zwei
/// Dienste dieselben Worte lesen: profile-service schreibt sie, scout-service
/// liest sie über die interne Suche. Zwei Schreibweisen für dieselbe Stufe
/// wären zwei Gelegenheiten, eine Aussage stillschweigend fallen zu lassen —
/// dieselbe Überlegung wie bei den Benachrichtigungsarten.</para>
///
/// <para><c>null</c> ist ein gültiger Wert auf beiden Seiten und heisst
/// „nichts gesagt". Es ist ausdrücklich <em>kein</em> Fehler: ein Profil ohne
/// diese Angabe ist vollständig (ADR-0041).</para>
/// </remarks>
public static class Pendelstufen
{
    /// <summary>Das Wort zur Stufe, oder <c>null</c>.</summary>
    public static string? Wort(Pendelbereitschaft? stufe) => stufe switch
    {
        Pendelbereitschaft.Bis10 => "bis_10",
        Pendelbereitschaft.Bis25 => "bis_25",
        Pendelbereitschaft.Bis50 => "bis_50",
        Pendelbereitschaft.Bis100 => "bis_100",
        Pendelbereitschaft.Egal => "egal",
        _ => null
    };

    /// <summary>Die Stufe zum Wort, oder <c>null</c>.</summary>
    /// <remarks>
    /// Ein unbekanntes Wort wird zu <c>null</c> und nicht zu einem Fehler: es
    /// ist eine freiwillige Angabe, und eine unlesbare Angabe ist dasselbe wie
    /// keine. Ein 422 an dieser Stelle hielte ein ganzes Formular auf, weil ein
    /// Wert falsch geschrieben war, den niemand ausfüllen musste.
    /// </remarks>
    public static Pendelbereitschaft? Lies(string? wort) => wort switch
    {
        "bis_10" => Pendelbereitschaft.Bis10,
        "bis_25" => Pendelbereitschaft.Bis25,
        "bis_50" => Pendelbereitschaft.Bis50,
        "bis_100" => Pendelbereitschaft.Bis100,
        "egal" => Pendelbereitschaft.Egal,
        _ => null
    };

    /// <summary>
    /// Wie viele Kilometer die Stufe deckt — <c>null</c> heisst „ohne Grenze".
    /// </summary>
    /// <remarks>
    /// <strong>Diese Zahl verlässt den Dienst nie.</strong> Sie steht hier, weil
    /// irgendwo aus „bis 50" und einer Entfernung ein Ja oder Nein werden muss,
    /// und sie wird an genau einer Stelle gelesen: beim Bilden des Häkchens.
    /// Sie gehört in keinen Vertrag, in keine Antwort und in keine Sortierung
    /// (ADR-0041).
    /// </remarks>
    public static int? Grenze(Pendelbereitschaft stufe) => stufe switch
    {
        Pendelbereitschaft.Bis10 => 10,
        Pendelbereitschaft.Bis25 => 25,
        Pendelbereitschaft.Bis50 => 50,
        Pendelbereitschaft.Bis100 => 100,
        _ => null
    };
}

/// <summary>Die Worte für die Umzugsbereitschaft.</summary>
public static class Umzugsworte
{
    /// <summary>Das Wort, oder <c>null</c>.</summary>
    public static string? Wort(Umzugsbereitschaft? wahl) => wahl switch
    {
        Umzugsbereitschaft.Ja => "ja",
        Umzugsbereitschaft.Nein => "nein",
        Umzugsbereitschaft.Offen => "offen",
        _ => null
    };

    /// <summary>Die Wahl zum Wort, oder <c>null</c>.</summary>
    public static Umzugsbereitschaft? Lies(string? wort) => wort switch
    {
        "ja" => Umzugsbereitschaft.Ja,
        "nein" => Umzugsbereitschaft.Nein,
        "offen" => Umzugsbereitschaft.Offen,
        _ => null
    };
}
