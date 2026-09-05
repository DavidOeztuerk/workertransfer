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
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    /// <summary>Welche Sprachen vorkommen — die Menge, nie ihr Anteil.</summary>
    /// <remarks>
    /// Ohne Bytes und ohne Prozente: daraus rechnete das gelöschte Paket sein
    /// „Können" (ADR-0022 §2). Was nicht da ist, kann niemand aufsummieren.
    /// </remarks>
    [property: JsonPropertyName("languages")] IReadOnlyList<string> Languages,
    /// <summary>Die Topics, die der Besitzer selbst gesetzt hat.</summary>
    [property: JsonPropertyName("topics")] IReadOnlyList<string> Topics);

/// <summary>Eine Verbindung, wie sie gelesen wird.</summary>
/// <remarks>
/// Kein Punktwert, kein Rang, keine abgeleitete Fähigkeit. Was hier steht, ist
/// eine Liste von Belegen mit Links — wer wissen will, ob der Code gut ist,
/// klickt darauf.
/// </remarks>
public sealed record VerbindungV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    /// <summary><c>null</c>: noch kein Konto genannt.</summary>
    /// <remarks>
    /// Wer die Anmeldung über GitHub begonnen hat, nennt kein Konto — GitHub
    /// meldet es. Bis der Rücksprung da ist, gibt es hier nichts zu zeigen,
    /// und einen leeren Text auszugeben wäre ein Name, der nicht existiert.
    /// </remarks>
    [property: JsonPropertyName("login")] string? Login,
    [property: JsonPropertyName("verified")] bool Verified,
    /// <summary>Nur in der eigenen Ansicht, und nur solange nicht bewiesen.</summary>
    /// <remarks>
    /// Die Einmalzeichenfolge nützt allein der Person, die den Gist anlegt.
    /// </remarks>
    [property: JsonPropertyName("challenge_description")] string? ChallengeDescription,
    [property: JsonPropertyName("fetched_at")] DateTimeOffset? FetchedAt,
    [property: JsonPropertyName("repositories")] IReadOnlyList<RepositoryV1> Repositories,
    /// <summary>Konnten für jedes Repository die Sprachen geholt werden?</summary>
    /// <remarks>
    /// <c>false</c> heißt: bei einigen steht nur die Hauptsprache, weil GitHubs
    /// Ratenlimit den Aufruf je Repository begrenzt. Die Oberfläche <em>muss</em>
    /// das sagen — eine Menge, die unvollständig ist und so tut, als wäre sie es
    /// nicht, ist die stillschweigende Vollständigkeit aus ADR-0022 §3.
    /// </remarks>
    [property: JsonPropertyName("languages_complete")] bool LanguagesComplete);

/// <summary>Wohin der Browser für die Anmeldung bei GitHub geht.</summary>
/// <remarks>
/// <c>url</c> ist <c>null</c>, wenn keine Anmeldung eingerichtet ist — dann
/// bleibt der Gist der Weg, und die Oberfläche bietet nur ihn an. Ein Knopf,
/// der auf eine Adresse zeigt, die es nicht gibt, wäre schlimmer als kein Knopf.
/// </remarks>
/// <summary>Ob dieser Server die Anmeldung über GitHub anbietet.</summary>
/// <remarks>
/// Eine Aussage über die <em>Einrichtung</em>, nicht über einen Menschen. Die
/// Oberfläche braucht sie, bevor jemand klickt: ohne sie müsste sie entweder
/// einen Knopf zeigen, der ins Leere führt, oder die Gist-Anleitung auch dort,
/// wo ein Knopf genügt.
/// </remarks>
public sealed record AnmeldungMoeglichV1(
    [property: JsonPropertyName("available")] bool Available);

public sealed record AnmeldebeginnV1(
    [property: JsonPropertyName("url")] string? Url);

/// <summary>Was aus GitHubs Rücksprung zurückkommt.</summary>
/// <remarks>
/// <c>state</c> ist die Einmalzeichenfolge, die wir mitgegeben haben. Sie muss
/// zurückgereicht werden: sie beweist, dass diese Antwort zu unserer Anfrage
/// gehört und nicht zu einer untergeschobenen.
/// </remarks>
public sealed record AnmeldungAbschliessenV1(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("state")] string State);

/// <summary>Was eine Person schickt, um ein Konto zu beanspruchen.</summary>
/// <remarks>
/// Ohne <c>subject_id</c>: wessen Verbindung es ist, steht im geprüften Token.
/// </remarks>
public sealed record VerbindenV1(
    [property: JsonPropertyName("login")] string Login);
