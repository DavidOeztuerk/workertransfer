namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Which working world a person stands in (ADR-0039).</summary>
/// <remarks>
/// A statement by the person from which exactly one thing follows: what the
/// interface offers. No permission, no visibility, no ordering. It is not in
/// the token.
/// <para>
/// The set is closed, because a navigation follows from this field and a
/// navigation has to have an answer for every possible value — as with
/// <see cref="AccountLanguage"/>. <see cref="Other"/> is a choice and not the
/// same thing as <c>null</c>.
/// </para>
/// </remarks>
public enum OccupationalField
{
    /// <summary>Crafts — metal, wood, plumbing, motor vehicles.</summary>
    Crafts,

    /// <summary>Industry and technology — production, maintenance, automation.</summary>
    IndustryTechnology,

    /// <summary>Construction — building, civil engineering, fit-out.</summary>
    Construction,

    /// <summary>Health and care.</summary>
    HealthCare,

    /// <summary>Logistics and transport — warehouse, driving, dispatch.</summary>
    LogisticsTransport,

    /// <summary>Hospitality and hotels.</summary>
    HospitalityHotel,

    /// <summary>Retail and sales.</summary>
    RetailSales,

    /// <summary>Office and administration.</summary>
    OfficeAdministration,

    /// <summary>IT and software.</summary>
    ItSoftware,

    /// <summary>Education and social work.</summary>
    EducationSocial,

    /// <summary>Something else — a choice, not an absence.</summary>
    Other
}

/// <summary>Reads an OccupationalField out of a label, and back.</summary>
/// <remarks>
/// The label is snake_case, like the whole wire. It is not derived from the
/// enum member name: <c>ToLower()</c> would give <c>industrytechnology</c>.
/// </remarks>
public static class OccupationalFieldChoice
{
    /// <summary>Label → field. The one place the list stands.</summary>
    private static readonly IReadOnlyDictionary<string, OccupationalField> ByLabel =
        new Dictionary<string, OccupationalField>(StringComparer.OrdinalIgnoreCase)
        {
            ["handwerk"] = OccupationalField.Crafts,
            ["industrie_technik"] = OccupationalField.IndustryTechnology,
            ["bau"] = OccupationalField.Construction,
            ["gesundheit_pflege"] = OccupationalField.HealthCare,
            ["logistik_verkehr"] = OccupationalField.LogisticsTransport,
            ["gastronomie_hotel"] = OccupationalField.HospitalityHotel,
            ["handel_verkauf"] = OccupationalField.RetailSales,
            ["buero_verwaltung"] = OccupationalField.OfficeAdministration,
            ["it_software"] = OccupationalField.ItSoftware,
            ["bildung_soziales"] = OccupationalField.EducationSocial,
            ["sonstiges"] = OccupationalField.Other
        };

    /// <summary>Every label, in the order of the enumeration.</summary>
    public static IReadOnlyList<string> Labels { get; } =
        [.. ByLabel.OrderBy(entry => entry.Value).Select(entry => entry.Key)];

    /// <summary>The field for a label — or <c>null</c>.</summary>
    /// <remarks>
    /// Anything unknown becomes <c>null</c> and never a default: guessing an
    /// occupational field would be the derived attribute ADR-0039 rejects.
    /// Whether an input was usable is answered by <see cref="Known"/>.
    /// </remarks>
    /// <param name="label">What came in.</param>
    /// <returns>The field, or <c>null</c>.</returns>
    public static OccupationalField? From(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return ByLabel.TryGetValue(label.Trim(), out var field) ? field : null;
    }

    /// <summary>Do we know this label?</summary>
    /// <remarks>
    /// Separate from <see cref="From"/>, so an endpoint can refuse something
    /// unknown instead of quietly turning it into <c>null</c>. Empty is
    /// permitted and means "none" — otherwise there would be no way back.
    /// </remarks>
    /// <param name="label">What came in.</param>
    /// <returns><c>true</c> if it may be stored.</returns>
    public static bool Known(string? label) =>
        string.IsNullOrWhiteSpace(label) || ByLabel.ContainsKey(label.Trim());

    /// <summary>The label as it stands in the column and on the wire.</summary>
    /// <param name="field">The field, or <c>null</c>.</param>
    /// <returns>snake_case — or <c>null</c> when nobody chose.</returns>
    public static string? Label(OccupationalField? field) => field switch
    {
        OccupationalField.Crafts => "handwerk",
        OccupationalField.IndustryTechnology => "industrie_technik",
        OccupationalField.Construction => "bau",
        OccupationalField.HealthCare => "gesundheit_pflege",
        OccupationalField.LogisticsTransport => "logistik_verkehr",
        OccupationalField.HospitalityHotel => "gastronomie_hotel",
        OccupationalField.RetailSales => "handel_verkauf",
        OccupationalField.OfficeAdministration => "buero_verwaltung",
        OccupationalField.ItSoftware => "it_software",
        OccupationalField.EducationSocial => "bildung_soziales",
        OccupationalField.Other => "sonstiges",
        _ => null
    };
}
