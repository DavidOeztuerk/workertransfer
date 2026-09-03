using System.Text.Json.Serialization;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>One page of a list, with everything a pager needs to draw itself.</summary>
/// <remarks>
/// <strong>Seitennummern statt Zeiger, und das ist ein Tausch mit einem Preis.</strong>
/// Ein Zeiger (`cursor`) ist stabil, wenn zwischen zwei Seiten etwas eingefügt
/// wird — eine Seitennummer nicht: erscheint eine neue Anzeige, rutscht auf
/// Seite 2 ein Eintrag von Seite 1 nach. Dafür kann eine Seitennummer etwas,
/// das ein Zeiger nicht kann: <em>springen</em>. Wer „Seite 7 von 14" sehen und
/// direkt dorthin will, braucht eine Gesamtzahl, und die gibt es nur mit einem
/// zweiten Zählen.
/// <para>
/// Für eine Stellenliste ist das der richtige Tausch: sie ändert sich in
/// Minuten, nicht in Sekunden, und „wie viele gibt es überhaupt" ist die Frage,
/// die jeder zuerst stellt.
/// </para>
/// <para>
/// <strong>Die Gesamtzahl zählt ANZEIGEN, keine Menschen.</strong> Auf einer
/// Kandidatenliste wäre dieselbe Zahl eine Aussage über Personen, und die
/// verbietet ADR-0026. Wer diesen Umschlag dort benutzt, lässt
/// <see cref="Gesamt"/> weg und schaltet auf <see cref="HatWeiter"/> zurück.
/// </para>
/// </remarks>
/// <typeparam name="T">Was auf der Seite steht.</typeparam>
/// <param name="Items">Die Einträge dieser Seite, in der Reihenfolge der Abfrage.</param>
/// <param name="Seite">Die aktuelle Seite, 1-basiert. Menschen zählen ab eins.</param>
/// <param name="Seitengroesse">Wie viele Einträge angefragt wurden.</param>
/// <param name="Gesamt">Wie viele Einträge es insgesamt gibt.</param>
public sealed record Seitenantwort<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("page")] int Seite,
    [property: JsonPropertyName("page_size")] int Seitengroesse,
    [property: JsonPropertyName("total_items")] int Gesamt)
{
    /// <summary>Wie viele Seiten es gibt. Mindestens eine, auch bei null Einträgen.</summary>
    /// <remarks>
    /// Eine leere Liste hat EINE Seite und nicht null: „Seite 1 von 0" ist eine
    /// Aussage, die niemand lesen kann, und ein Pager mit null Seiten hat keinen
    /// Zustand, den er zeichnen könnte.
    /// </remarks>
    [JsonPropertyName("total_pages")]
    public int Seiten => Math.Max(1, (int)Math.Ceiling(Gesamt / (double)Math.Max(1, Seitengroesse)));

    /// <summary>Gibt es eine Seite danach?</summary>
    [JsonPropertyName("has_next")]
    public bool HatWeiter => Seite < Seiten;

    /// <summary>Gibt es eine Seite davor?</summary>
    [JsonPropertyName("has_previous")]
    public bool HatZurueck => Seite > 1;
}

/// <summary>Liest Seite und Seitengrösse aus einer Abfrage — an einer Stelle.</summary>
/// <remarks>
/// <strong>Die Grenzen sind hier und nicht im Endpunkt.</strong> Ein
/// <c>page_size=100000</c> ist keine Anfrage, sondern ein Weg, die Datenbank zu
/// beschäftigen; und drei Endpunkte, die sich ihre eigene Obergrenze ausdenken,
/// haben drei verschiedene.
/// <para>
/// Unsinn wird zur Vorgabe und nicht zu einem Fehler: eine Liste, die auf
/// <c>?page=abc</c> mit 400 antwortet, ist an einer Stelle streng, an der
/// niemand etwas gewinnt.
/// </para>
/// </remarks>
public static class Seitenwahl
{
    /// <summary>Wie viele Einträge, wenn niemand etwas sagt.</summary>
    /// <remarks>
    /// Zwölf, weil eine Kartenliste in zwei, drei und vier Spalten aufgeht —
    /// zehn oder zwanzig lassen in der letzten Zeile eine Lücke.
    /// </remarks>
    public const int VorgabeGroesse = 12;

    /// <summary>Die Obergrenze. Darüber ist es kein Blättern mehr.</summary>
    public const int HoechsteGroesse = 96;

    /// <summary>Die Seite, 1-basiert. Unsinn wird 1.</summary>
    /// <param name="roh">Was in der Abfrage stand.</param>
    /// <returns>Eine Seitennummer ab 1.</returns>
    public static int Seite(string? roh) =>
        int.TryParse(roh, out var wert) && wert > 0 ? wert : 1;

    /// <summary>Die Seitengrösse, begrenzt. Unsinn wird die Vorgabe.</summary>
    /// <param name="roh">Was in der Abfrage stand.</param>
    /// <returns>Eine Grösse zwischen 1 und <see cref="HoechsteGroesse"/>.</returns>
    public static int Groesse(string? roh) =>
        int.TryParse(roh, out var wert) && wert > 0
            ? Math.Min(wert, HoechsteGroesse)
            : VorgabeGroesse;
}
