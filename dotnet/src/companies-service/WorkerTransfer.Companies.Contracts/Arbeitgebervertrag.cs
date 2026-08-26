using System.Text.Json.Serialization;

namespace WorkerTransfer.Companies.Contracts;

/// <summary>Ein Arbeitgeberprofil, wie es gelesen wird.</summary>
public sealed record ArbeitgeberprofilV1(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("about")] string About,
    [property: JsonPropertyName("website")] string? Website,
    [property: JsonPropertyName("locations")] IReadOnlyList<string> Locations,
    [property: JsonPropertyName("benefits")] IReadOnlyList<string> Benefits,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Ein Arbeitgeberprofil, wie es geschrieben wird.</summary>
/// <remarks>
/// Ohne <c>tenant_id</c> — wem das Profil gehört, steht im geprüften Token —
/// und ohne <c>slug</c>: das Kürzel vergibt der Server beim ersten Speichern
/// und ändert es danach nie. Ein Feld dafür wäre eine Einladung, fremde Namen
/// zu besetzen, und jede spätere Änderung bräche einen geteilten Link.
/// </remarks>
public sealed record ArbeitgeberprofilSchreibenV1(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("about")] string About = "",
    [property: JsonPropertyName("website")] string? Website = null,
    [property: JsonPropertyName("locations")] IReadOnlyList<string>? Locations = null,
    [property: JsonPropertyName("benefits")] IReadOnlyList<string>? Benefits = null);
