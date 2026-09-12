using System.Text.Json.Serialization;

namespace WorkerTransfer.Portfolio.Contracts;

/// <summary>Ein Eintrag auf der Leitung.</summary>
/// <remarks>
/// Die Feldnamen sind snake_case und bleiben es. Die Browser-App wurde gegen
/// den Python-Dienst geschrieben, und eine Grenze, die ihre Felder bei einer
/// Migration umbenennt, bricht jeden Aufrufer ohne Gegenwert.
/// <para>
/// Trägt <b>keine Sichtbarkeit</b> und darf keine bekommen: ob ein Portfolio
/// gezeigt werden darf, steht nur im Consent-Ledger (ADR-0020 §6).
/// </para>
/// </remarks>
public sealed record EintragV1(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary = "",
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("role")] string Role = "",
    [property: JsonPropertyName("year")] int? Year = null,
    [property: JsonPropertyName("attachment")] string? Attachment = null,
    /// <summary>Womit gearbeitet wurde — von der Person selbst genannt.</summary>
    /// <remarks>
    /// Vereinheitlicht durch denselben Wortschatz wie das Profil (ADR-0023).
    /// Sie machen die Arbeit nicht durchsuchbar: suchbar wird eine Fähigkeit
    /// erst im Profil, und dorthin kommt sie mit einem Klick.
    /// </remarks>
    [property: JsonPropertyName("technologies")] IReadOnlyList<string>? Technologies = null);

/// <summary>Ein Portfolio, wie es gelesen wird.</summary>
public sealed record PortfolioV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("items")] IReadOnlyList<EintragV1> Items,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

/// <summary>Ein Portfolio, wie es geschrieben wird.</summary>
/// <remarks>
/// Ohne <c>subject_id</c>: wessen Portfolio das ist, steht im geprüften Token.
/// </remarks>
public sealed record PortfolioSchreibenV1(
    [property: JsonPropertyName("items")] IReadOnlyList<EintragV1> Items);
