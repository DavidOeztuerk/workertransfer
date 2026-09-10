namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>In welcher Arbeitswelt eine Person steht (ADR-0039).</summary>
/// <remarks>
/// <strong>Eine Angabe der Person, aus der genau eine Sache folgt: was die
/// Oberfläche anbietet.</strong> Keine Berechtigung, keine Sichtbarkeit, keine
/// Sortierung, keine Aussage über einen Menschen. Es steht nicht im Token, und
/// keine Berechtigungsprüfung liest es.
/// <para>
/// <strong>Die Menge ist geschlossen, und das ist der Grund für die Menge.</strong>
/// Aus diesem Feld folgt eine Navigation, und eine Navigation muss für jeden
/// möglichen Wert eine Antwort haben. Bei Freitext hiesse die Antwort für alles
/// ausser einer Handvoll Schreibweisen „ich weiss es nicht" — und der
/// Unterschied zwischen „ich weiss es nicht" und „nicht angegeben" wäre für die
/// Oberfläche unsichtbar. Sie zeigte demselben Menschen je nach Tippfehler zwei
/// verschiedene Anwendungen. Dieselbe Begründung trägt schon
/// <see cref="Kontosprache"/>.
/// </para>
/// <para>
/// <strong><see cref="Sonstiges"/> ist nicht dasselbe wie <c>null</c>.</strong>
/// Wer <c>sonstiges</c> wählt, hat gewählt: seine Arbeit passt in keine der
/// zehn. Wer nichts wählt, hat nicht gewählt. Beide bekommen heute dieselbe
/// neutrale Ansicht — aber weil das für beide die richtige ist, nicht weil sie
/// dasselbe wären.
/// </para>
/// <para>
/// Erweiterbar, und jede Erweiterung ist eine Entscheidung: ein Pull Request,
/// wie beim Wortschatz. Sie darf nicht aus den Daten wachsen („diese Wörter
/// tippen viele") — das wäre wieder eine Auswertung über Menschen (ADR-0022).
/// </para>
/// </remarks>
public enum Berufsfeld
{
    /// <summary>Handwerk — Metall, Holz, Sanitär, Kfz.</summary>
    Handwerk,

    /// <summary>Industrie und Technik — Fertigung, Instandhaltung, Automatisierung.</summary>
    IndustrieTechnik,

    /// <summary>Bau — Hoch- und Tiefbau, Ausbau.</summary>
    Bau,

    /// <summary>Gesundheit und Pflege.</summary>
    GesundheitPflege,

    /// <summary>Logistik und Verkehr — Lager, Fahren, Disposition.</summary>
    LogistikVerkehr,

    /// <summary>Gastronomie und Hotel.</summary>
    GastronomieHotel,

    /// <summary>Handel und Verkauf.</summary>
    HandelVerkauf,

    /// <summary>Büro und Verwaltung.</summary>
    BueroVerwaltung,

    /// <summary>IT und Software.</summary>
    ItSoftware,

    /// <summary>Bildung und Soziales.</summary>
    BildungSoziales,

    /// <summary>Etwas anderes — eine Wahl, kein Fehlen.</summary>
    Sonstiges
}

/// <summary>Liest ein Berufsfeld aus einem Etikett und zurück.</summary>
/// <remarks>
/// Getrennt vom Aufzählungstyp, wie <see cref="Sprachwahl"/> von
/// <see cref="Kontosprache"/>: das Etikett ist, was in der Spalte und auf dem
/// Draht steht, und es ist snake_case, weil der Draht dieser Plattform das ist.
/// Der Name der Aufzählung folgt C#, das Etikett folgt der Leitung, und keiner
/// von beiden leitet sich automatisch aus dem anderen ab — eine Umwandlung
/// über <c>ToLower()</c> ergäbe <c>industrietechnik</c> und nicht
/// <c>industrie_technik</c>.
/// </remarks>
public static class Berufsfeldwahl
{
    /// <summary>Etikett → Feld. Die einzige Stelle, an der die Liste steht.</summary>
    private static readonly IReadOnlyDictionary<string, Berufsfeld> NachEtikett =
        new Dictionary<string, Berufsfeld>(StringComparer.OrdinalIgnoreCase)
        {
            ["handwerk"] = Berufsfeld.Handwerk,
            ["industrie_technik"] = Berufsfeld.IndustrieTechnik,
            ["bau"] = Berufsfeld.Bau,
            ["gesundheit_pflege"] = Berufsfeld.GesundheitPflege,
            ["logistik_verkehr"] = Berufsfeld.LogistikVerkehr,
            ["gastronomie_hotel"] = Berufsfeld.GastronomieHotel,
            ["handel_verkauf"] = Berufsfeld.HandelVerkauf,
            ["buero_verwaltung"] = Berufsfeld.BueroVerwaltung,
            ["it_software"] = Berufsfeld.ItSoftware,
            ["bildung_soziales"] = Berufsfeld.BildungSoziales,
            ["sonstiges"] = Berufsfeld.Sonstiges
        };

    /// <summary>Alle Etiketten, in der Reihenfolge der Aufzählung.</summary>
    /// <remarks>
    /// Für die Prüfung, die die Menge festhält, und für jeden, der die Liste
    /// ausgeben will, ohne sie ein zweites Mal zu tippen.
    /// </remarks>
    public static IReadOnlyList<string> Etiketten { get; } =
        [.. NachEtikett.OrderBy(eintrag => eintrag.Value).Select(eintrag => eintrag.Key)];

    /// <summary>Das Feld zu einem Etikett — oder <c>null</c>, wenn es keins gibt.</summary>
    /// <remarks>
    /// <strong>Unbekanntes wird <c>null</c> und niemals eine Vorgabe.</strong>
    /// Hier gibt es keine, die richtig sein könnte: ein Berufsfeld zu raten
    /// wäre genau die abgeleitete Eigenschaft, die ADR-0039 verworfen hat. Wer
    /// entscheiden will, ob eine Eingabe taugte, fragt
    /// <see cref="Kennen"/> — dort ist <c>null</c> eine Absage und keine stille
    /// Ersetzung.
    /// </remarks>
    /// <param name="etikett">Was hereinkam.</param>
    /// <returns>Das Feld, oder <c>null</c>.</returns>
    public static Berufsfeld? Aus(string? etikett)
    {
        if (string.IsNullOrWhiteSpace(etikett))
        {
            return null;
        }

        return NachEtikett.TryGetValue(etikett.Trim(), out var feld) ? feld : null;
    }

    /// <summary>Kennen wir dieses Etikett?</summary>
    /// <remarks>
    /// Getrennt von <see cref="Aus"/>, damit ein Endpunkt eine unbekannte
    /// Eingabe absagen kann, statt sie stillschweigend zu <c>null</c> zu
    /// machen. Eine Wahl, die nicht wirkt, muss das sagen — dieselbe Regel wie
    /// bei <c>PUT /account/language</c>.
    /// <para>
    /// Ein leeres Etikett ist <em>bekannt</em>: es heisst „keins", und das ist
    /// ein zulässiger Wert. Sonst gäbe es keinen Weg zurück zu „nicht
    /// angegeben".
    /// </para>
    /// </remarks>
    /// <param name="etikett">Was hereinkam.</param>
    /// <returns><c>true</c>, wenn es gespeichert werden darf.</returns>
    public static bool Kennen(string? etikett) =>
        string.IsNullOrWhiteSpace(etikett) || NachEtikett.ContainsKey(etikett.Trim());

    /// <summary>Das Etikett, wie es in der Spalte und auf dem Draht steht.</summary>
    /// <param name="feld">Das Feld, oder <c>null</c>.</param>
    /// <returns>snake_case — oder <c>null</c>, wenn niemand gewählt hat.</returns>
    public static string? Etikett(Berufsfeld? feld) => feld switch
    {
        Berufsfeld.Handwerk => "handwerk",
        Berufsfeld.IndustrieTechnik => "industrie_technik",
        Berufsfeld.Bau => "bau",
        Berufsfeld.GesundheitPflege => "gesundheit_pflege",
        Berufsfeld.LogistikVerkehr => "logistik_verkehr",
        Berufsfeld.GastronomieHotel => "gastronomie_hotel",
        Berufsfeld.HandelVerkauf => "handel_verkauf",
        Berufsfeld.BueroVerwaltung => "buero_verwaltung",
        Berufsfeld.ItSoftware => "it_software",
        Berufsfeld.BildungSoziales => "bildung_soziales",
        Berufsfeld.Sonstiges => "sonstiges",
        _ => null
    };
}
