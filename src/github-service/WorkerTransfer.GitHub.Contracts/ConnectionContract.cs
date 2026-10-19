using System.Text.Json.Serialization;

namespace WorkerTransfer.GitHub.Contracts;

/// <summary>A repository as GitHub reports it.</summary>
/// <remarks>
/// Every field comes from GitHub, none is computed. <c>stars</c> stands here
/// because GitHub reports it — it is <strong>no sort key</strong> and must
/// never become one.
/// </remarks>
public sealed record RepositoryV1(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    /// <summary>Which languages occur — the set, never their share.</summary>
    /// <remarks>
    /// Without bytes and without percentages: out of those the deleted package
    /// computed its "skill" (ADR-0022 §2). What is not there nobody can add up.
    /// </remarks>
    [property: JsonPropertyName("languages")] IReadOnlyList<string> Languages,
    /// <summary>The topics the owner set themselves.</summary>
    [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics);

/// <summary>A connection as it is read.</summary>
/// <remarks>
/// No score, no rank, no derived skill. What stands here is a list of evidence
/// with links — whoever wants to know whether the code is good clicks on it.
/// </remarks>
public sealed record ConnectionV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    /// <summary><c>null</c>: no account named yet.</summary>
    /// <remarks>
    /// Whoever began the sign-in over GitHub names no account — GitHub reports
    /// it. Until the return is there, there is nothing to show here, and giving
    /// out an empty text would be a name that does not exist.
    /// </remarks>
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("verified")] bool Verified,
    /// <summary>Only in one's own view, and only for as long as it is not proved.</summary>
    /// <remarks>
    /// The one-time string is of use only to the person creating the gist.
    /// </remarks>
    [property: JsonPropertyName("challenge_description")] string? ChallengeDescription,
    [property: JsonPropertyName("fetched_at")] DateTimeOffset? FetchedAt,
    [property: JsonPropertyName("repositories")] IReadOnlyList<RepositoryV1> Repositories,
    /// <summary>Could the languages be fetched for every repository?</summary>
    /// <remarks>
    /// <c>false</c> means: at some of them only the main language stands,
    /// because GitHub's rate limit caps the call per repository. The interface
    /// <em>has to</em> say so — a set that is incomplete and acts as though it
    /// were not is the silent completeness from ADR-0022 §3.
    /// </remarks>
    [property: JsonPropertyName("languages_complete")] bool LanguagesComplete);

/// <summary>Whether this server offers the sign-in over GitHub.</summary>
/// <remarks>
/// A statement about the <em>configuration</em>, not about a human being. The
/// interface needs it before anybody clicks: without it, it would have to show
/// either a button leading nowhere or the gist instructions even where a button
/// is enough.
/// </remarks>
public sealed record SignInPossibleV1(
    [property: JsonPropertyName("available")] bool Available);

/// <summary>Where the browser goes for the sign-in at GitHub.</summary>
/// <remarks>
/// <c>url</c> is <c>null</c> when no sign-in is configured — then the gist
/// stays the path, and the interface offers only it. A button pointing at an
/// address that does not exist would be worse than no button.
/// </remarks>
public sealed record SignInStartV1(
    [property: JsonPropertyName("url")] string? Url);

/// <summary>What comes back out of GitHub's return.</summary>
/// <remarks>
/// <c>state</c> is the one-time string we sent along. It has to be handed back:
/// it proves this answer belongs to our request and not to a slipped-in one.
/// </remarks>
public sealed record CompleteSignInV1(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("state")] string State);

/// <summary>What a person sends to claim an account.</summary>
/// <remarks>
/// Without <c>subject_id</c>: whose connection it is stands in the verified
/// token.
/// </remarks>
public sealed record ConnectV1(
    [property: JsonPropertyName("login")] string Login);
