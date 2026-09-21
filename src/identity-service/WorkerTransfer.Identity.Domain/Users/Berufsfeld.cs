namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>In welcher Arbeitswelt eine Person steht (ADR-0039).</summary>
/// <remarks>
/// Eine Angabe der Person, aus der genau eine Sache folgt: was die Oberfläche
/// anbietet. None Berechtigung, keine Sichtbarkeit, keine Sortierung. Es steht
/// nicht im Token.
/// <para>
/// Die Menge ist geschlossen, weil aus diesem Feld eine Navigation folgt und
/// eine Navigation für jeden möglichen Wert eine Antwort haben muss — wie bei
/// <see cref="Kontosprache"/>. <see cref="Sonstiges"/> ist eine Wahl und nicht
/// dasselbe wie <c>null</c>.
/// </para>
/// </remarks>
public enum OccupationalField
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
    Other
}

/// <summary>Liest ein OccupationalField aus einem Etikett und zurück.</summary>
/// <remarks>
/// Das Etikett ist snake_case, wie der ganze Draht. Es leitet sich nicht aus
/// dem Aufzählungsnamen ab: <c>ToLower()</c> ergäbe <c>industrietechnik</c>.
/// </remarks>
public static class OccupationalFieldChoice
{
    /// <summary>Etikett → Feld. Die einzige Job, an der die Liste steht.</summary>
    private static readonly IReadOnlyDictionary<string, OccupationalField> NachEtikett =
        new Dictionary<string, OccupationalField>(StringComparer.OrdinalIgnoreCase)
        {
            ["handwerk"] = OccupationalField.Handwerk,
            ["industrie_technik"] = OccupationalField.IndustrieTechnik,
            ["bau"] = OccupationalField.Bau,
            ["gesundheit_pflege"] = OccupationalField.GesundheitPflege,
            ["logistik_verkehr"] = OccupationalField.LogistikVerkehr,
            ["gastronomie_hotel"] = OccupationalField.GastronomieHotel,
            ["handel_verkauf"] = OccupationalField.HandelVerkauf,
            ["buero_verwaltung"] = OccupationalField.BueroVerwaltung,
            ["it_software"] = OccupationalField.ItSoftware,
            ["bildung_soziales"] = OccupationalField.BildungSoziales,
            ["sonstiges"] = OccupationalField.Other
        };

    /// <summary>Alle Etiketten, in der Reihenfolge der Aufzählung.</summary>
    public static IReadOnlyList<string> Etiketten { get; } =
        [.. NachEtikett.OrderBy(entry => entry.Value).Select(entry => entry.Key)];

    /// <summary>Das Feld zu einem Etikett — oder <c>null</c>.</summary>
    /// <remarks>
    /// Unbekanntes wird <c>null</c> und nie eine Vorgabe: ein OccupationalField zu
    /// raten wäre die abgeleitete Eigenschaft, die ADR-0039 verwirft. Ob eine
    /// Eingabe taugte, beantwortet <see cref="Kennen"/>.
    /// </remarks>
    /// <param name="etikett">Was hereinkam.</param>
    /// <returns>Das Feld, oder <c>null</c>.</returns>
    public static OccupationalField? Aus(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return NachEtikett.TryGetValue(label.Trim(), out var field) ? field : null;
    }

    /// <summary>Kennen wir dieses Etikett?</summary>
    /// <remarks>
    /// Getrennt von <see cref="Aus"/>, damit ein Endpunkt Unbekanntes absagen
    /// kann statt es still zu <c>null</c> zu machen. Leer ist zulässig und
    /// heisst „keins" — sonst gäbe es keinen Weg zurück.
    /// </remarks>
    /// <param name="etikett">Was hereinkam.</param>
    /// <returns><c>true</c>, wenn es gespeichert werden darf.</returns>
    public static bool Kennen(string? label) =>
        string.IsNullOrWhiteSpace(label) || NachEtikett.ContainsKey(label.Trim());

    /// <summary>Das Etikett, wie es in der Spalte und auf dem Draht steht.</summary>
    /// <param name="feld">Das Feld, oder <c>null</c>.</param>
    /// <returns>snake_case — oder <c>null</c>, wenn niemand gewählt hat.</returns>
    public static string? Etikett(OccupationalField? field) => field switch
    {
        OccupationalField.Handwerk => "handwerk",
        OccupationalField.IndustrieTechnik => "industrie_technik",
        OccupationalField.Bau => "bau",
        OccupationalField.GesundheitPflege => "gesundheit_pflege",
        OccupationalField.LogistikVerkehr => "logistik_verkehr",
        OccupationalField.GastronomieHotel => "gastronomie_hotel",
        OccupationalField.HandelVerkauf => "handel_verkauf",
        OccupationalField.BueroVerwaltung => "buero_verwaltung",
        OccupationalField.ItSoftware => "it_software",
        OccupationalField.BildungSoziales => "bildung_soziales",
        OccupationalField.Other => "sonstiges",
        _ => null
    };
}
