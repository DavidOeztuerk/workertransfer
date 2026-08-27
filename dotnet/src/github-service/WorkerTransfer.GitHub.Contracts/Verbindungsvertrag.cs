using System.Text.Json.Serialization;

namespace WorkerTransfer.GitHub.Contracts;

/// <summary>Ein Repository, wie GitHub es meldet.</summary>
/// <remarks>
/// Jedes Feld kommt von GitHub, keins ist gerechnet. <c>stars</c> steht hier,
/// weil GitHub es meldet — es ist <strong>kein Sortierschlüssel</strong> und
/// darf keiner werden.
/// </remarks>
public sealed record RepositoryV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt);

/// <summary>Eine Verbindung, wie sie gelesen wird.</summary>
/// <remarks>
/// Kein Punktwert, kein Rang, keine abgeleitete Fähigkeit. Was hier steht, ist
/// eine Liste von Belegen mit Links — wer wissen will, ob der Code gut ist,
/// klickt darauf.
/// </remarks>
public sealed record VerbindungV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("verified")] bool Verified,
    /// <summary>Nur in der eigenen Ansicht, und nur solange nicht bewiesen.</summary>
    /// <remarks>
    /// Die Einmalzeichenfolge nützt allein der Person, die den Gist anlegt.
    /// </remarks>
    [property: JsonPropertyName("challenge_description")] string? ChallengeDescription,
    [property: JsonPropertyName("fetched_at")] DateTimeOffset? FetchedAt,
    [property: JsonPropertyName("repositories")] IReadOnlyList<RepositoryV1> Repositories);

/// <summary>Was eine Person schickt, um ein Konto zu beanspruchen.</summary>
/// <remarks>
/// Ohne <c>subject_id</c>: wessen Verbindung es ist, steht im geprüften Token.
/// </remarks>
public sealed record VerbindenV1(
    [property: JsonPropertyName("login")] string Login);
