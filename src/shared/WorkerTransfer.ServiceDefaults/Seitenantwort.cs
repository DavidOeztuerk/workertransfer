using System.Text.Json.Serialization;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>One page of a list, with everything a pager needs to draw itself.</summary>
/// <remarks>
/// <strong>Page numbers instead of a cursor, and that is a trade with a price.</strong>
/// A cursor (`cursor`) is stable when something is inserted between two pages —
/// a page number is not: if a new advert appears, an entry from page 1 slips
/// down onto page 2. In return a page number can do something a cursor cannot:
/// <em>jump</em>. Whoever wants to see "page 7 of 14" and go straight there
/// needs a total, and there is no total without counting a second time.
/// <para>
/// For a job list that is the right trade: it changes in minutes, not in
/// seconds, and "how many are there at all" is the question everybody asks
/// first.
/// </para>
/// <para>
/// <strong>The total counts ADVERTS, not people.</strong> On a candidate list
/// the same number would be a statement about persons, and ADR-0026 forbids
/// that. Whoever uses this envelope there leaves <see cref="Gesamt"/> out and
/// falls back on <see cref="HatWeiter"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">What stands on the page.</typeparam>
/// <param name="Items">The entries of this page, in the order of the query.</param>
/// <param name="Seite">The current page, 1-based. People count from one.</param>
/// <param name="Seitengroesse">How many entries were asked for.</param>
/// <param name="Gesamt">How many entries there are in total.</param>
/// <param name="Ausgelassen">
/// How many entries a filter <em>could not judge</em> and therefore left out.
/// <c>null</c> when the question did not arise — then the field does not stand
/// in the answer either.
/// <para>
/// <strong>It stands here so that the interface CAN say it.</strong> A filter
/// that cannot judge part of the set otherwise delivers a result that looks
/// complete and is not — the same lie by omission that ADR-0022 §3 forbids. The
/// first use is the radius search for jobs: adverts with an unknown location
/// fall out, and whoever chooses "25 km around me" has to learn that no
/// judgement was made about those.
/// </para>
/// </param>
public sealed record PageResponse<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("page")] int Seite,
    [property: JsonPropertyName("page_size")] int Seitengroesse,
    [property: JsonPropertyName("total_items")] int Gesamt,
    [property: JsonPropertyName("omitted")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Ausgelassen = null)
{
    /// <summary>How many pages there are. At least one, even with zero entries.</summary>
    /// <remarks>
    /// An empty list has ONE page and not zero: "page 1 of 0" is a statement
    /// nobody can read, and a pager with zero pages has no state it could draw.
    /// </remarks>
    [JsonPropertyName("total_pages")]
    public int Seiten => Math.Max(1, (int)Math.Ceiling(Gesamt / (double)Math.Max(1, Seitengroesse)));

    /// <summary>Is there a page after this one?</summary>
    [JsonPropertyName("has_next")]
    public bool HatWeiter => Seite < Seiten;

    /// <summary>Is there a page before this one?</summary>
    [JsonPropertyName("has_previous")]
    public bool HatZurueck => Seite > 1;
}

/// <summary>Reads page and page size out of a query — in one place.</summary>
/// <remarks>
/// <strong>The limits are here and not in the endpoint.</strong> A
/// <c>page_size=100000</c> is not a request but a way of keeping the database
/// busy; and three endpoints that invent their own ceiling have three different
/// ones.
/// <para>
/// Nonsense becomes the default and not an error: a list that answers
/// <c>?page=abc</c> with a 400 is strict in a place where nobody gains
/// anything.
/// </para>
/// </remarks>
public static class PageSelection
{
    /// <summary>How many entries when nobody says anything.</summary>
    /// <remarks>
    /// Twelve, because a card list divides evenly into two, three and four
    /// columns — ten or twenty leave a gap in the last row.
    /// </remarks>
    public const int VorgabeGroesse = 12;

    /// <summary>The ceiling. Above it, it is no longer paging.</summary>
    public const int HoechsteGroesse = 96;

    /// <summary>The page, 1-based. Nonsense becomes 1.</summary>
    /// <param name="roh">What stood in the query.</param>
    /// <returns>A page number from 1 upwards.</returns>
    public static int Seite(string? roh) =>
        int.TryParse(roh, out var value) && value > 0 ? value : 1;

    /// <summary>The page size, capped. Nonsense becomes the default.</summary>
    /// <param name="roh">What stood in the query.</param>
    /// <returns>A size between 1 and <see cref="HoechsteGroesse"/>.</returns>
    public static int Groesse(string? roh) =>
        int.TryParse(roh, out var value) && value > 0
            ? Math.Min(value, HoechsteGroesse)
            : VorgabeGroesse;
}
