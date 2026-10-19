using System.Text.Json.Serialization;

namespace WorkerTransfer.Companies.Contracts;

/// <summary>An employer profile as it is read.</summary>
public sealed record EmployerProfileV1(
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("about")] string About,
    [property: JsonPropertyName("website")] string? Website,
    [property: JsonPropertyName("locations")] IReadOnlyList<string> Locations,
    [property: JsonPropertyName("benefits")] IReadOnlyList<string> Benefits,
    [property: JsonPropertyName("line1")] string Line1,
    [property: JsonPropertyName("postal_code")] string PostalCode,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>An employer profile as it is written.</summary>
/// <remarks>
/// Without <c>tenant_id</c> — whom the profile belongs to stands in the
/// verified token — and without <c>slug</c>: the server assigns the slug at
/// the first save and never changes it afterwards. A field for it would be an
/// invitation to occupy somebody else's name, and every later change would
/// break a shared link.
/// </remarks>
public sealed record WriteEmployerProfileV1(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("about")] string About = "",
    [property: JsonPropertyName("website")] string? Website = null,
    [property: JsonPropertyName("locations")] IReadOnlyList<string>? Locations = null,
    [property: JsonPropertyName("benefits")] IReadOnlyList<string>? Benefits = null,
    [property: JsonPropertyName("line1")] string? Line1 = null,
    [property: JsonPropertyName("postal_code")] string? PostalCode = null,
    [property: JsonPropertyName("city")] string? City = null,
    [property: JsonPropertyName("country")] string? Country = null,
    [property: JsonPropertyName("phone")] string? Phone = null);
