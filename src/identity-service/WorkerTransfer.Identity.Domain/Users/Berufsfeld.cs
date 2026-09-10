namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>In welcher Arbeitswelt eine Person steht (ADR-0039).</summary>
/// <remarks>
/// Eine Angabe der Person, aus der genau eine Sache folgt: was die Oberfläche
/// anbietet. Keine Berechtigung, keine Sichtbarkeit, keine Sortierung. Es steht
/// nicht im Token.
/// <para>
/// Die Menge ist geschlossen, weil aus diesem Feld eine Navigation folgt und
/// eine Navigation für jeden möglichen Wert eine Antwort haben muss — wie bei
/// <see cref="Kontosprache"/>. <see cref="Sonstiges"/> ist eine Wahl und nicht
/// dasselbe wie <c>null</c>.
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
/// Das Etikett ist snake_case, wie der ganze Draht. Es leitet sich nicht aus
/// dem Aufzählungsnamen ab: <c>ToLower()</c> ergäbe <c>industrietechnik</c>.
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
    public static IReadOnlyList<string> Etiketten { get; } =
        [.. NachEtikett.OrderBy(eintrag => eintrag.Value).Select(eintrag => eintrag.Key)];

    /// <summary>Das Feld zu einem Etikett — oder <c>null</c>.</summary>
    /// <remarks>
    /// Unbekanntes wird <c>null</c> und nie eine Vorgabe: ein Berufsfeld zu
    /// raten wäre die abgeleitete Eigenschaft, die ADR-0039 verwirft. Ob eine
    /// Eingabe taugte, beantwortet <see cref="Kennen"/>.
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
    /// Getrennt von <see cref="Aus"/>, damit ein Endpunkt Unbekanntes absagen
    /// kann statt es still zu <c>null</c> zu machen. Leer ist zulässig und
    /// heisst „keins" — sonst gäbe es keinen Weg zurück.
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
