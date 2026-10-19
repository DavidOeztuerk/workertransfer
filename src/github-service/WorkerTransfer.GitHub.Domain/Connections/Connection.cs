using System.Security.Cryptography;
using Noelia.Core.Identity;

namespace WorkerTransfer.GitHub.Domain.Connections;

/// <summary>The user name is no good.</summary>
public sealed class LoginError(string was) : Exception($"GitHub login {was}");

/// <summary>This connection is not verified yet.</summary>
public sealed class NotVerified() : Exception("This connection is not verified yet");

/// <summary>This connection is already verified.</summary>
public sealed class AlreadyVerified() : Exception("This connection is already verified");

/// <summary>This connection already names an account.</summary>
public sealed class AlreadyNamed() : Exception("This connection already names an account");

/// <summary>One piece of evidence. Every field comes from GitHub, none is computed.</summary>
/// <remarks>
/// <c>Stars</c> stands here because GitHub reports it — <em>passed on, not
/// evaluated</em>. It is no sort key and must never become one: an ordering of
/// human beings by stars would be the ADR-0022 score through the back door,
/// even if it were called "activity".
/// </remarks>
/// <param name="Languages">
/// Which languages occur in this repository — the SET, never their share.
/// </param>
/// <remarks>
/// <strong>The set and not the bytes, and that is the whole difference.</strong>
/// Under <c>/languages</c> GitHub delivers a byte count per language. Out of
/// exactly that the deleted package computed its "skill": <c>bytes /
/// total_bytes</c> — "a checked-in dependency beats every careful library"
/// (ADR-0022 §2). Not storing the numbers in the first place is cheaper than
/// defending them later: what is not there nobody can add up.
/// </remarks>
/// <param name="Topics">
/// The topics the owner set on the repository themselves.
/// </param>
/// <remarks>
/// The strongest evidence of all, because it is a <em>naming</em>: "react",
/// "kubernetes" — a human being wrote that there, no counter derived it.
/// </remarks>
public sealed record Repository(
    string Name,
    string Description,
    string? Language,
    int Stars,
    string Url,
    DateTimeOffset? LastPushedAt,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Topics);

/// <summary>A snapshot: the evidence and how complete it is.</summary>
/// <param name="Repositories">The evidence itself.</param>
/// <param name="LanguagesComplete">
/// Whether the languages could be fetched for <em>every</em> repository.
/// </param>
/// <remarks>
/// <strong>The second field is no ornament but ADR-0022 §3.</strong> GitHub
/// reports a repository's languages only one at a time, one call per
/// repository, and without a token sixty requests an hour are allowed. At some
/// point that is the end — and then only the main language stands at the rest.
/// <em>Not</em> saying so would be exactly the silent completeness the ADR
/// forbids: "Python" would then read like "only Python", and the human being
/// behind it would look narrower than they are.
/// <para>
/// It sits on the snapshot and not on <see cref="Repository" />, because it is
/// a statement about our <em>fetch</em> and not about the repository. That
/// type's eight fields are all copied from GitHub, and <c>Adr0022Tests</c> pins
/// it.
/// </para>
/// </remarks>
public sealed record Snapshot(
    IReadOnlyList<Repository> Repositories,
    bool LanguagesComplete);

/// <summary>The connection to a GitHub account — proved, not asserted.</summary>
/// <remarks>
/// <strong>What does not stand here is the point</strong> (ADR-0022): no score,
/// no derived attributes, no "skill" out of bytes. A repository is evidence
/// with a link; whoever wants to know whether the code is good clicks on it.
/// That is the only honest evaluation this system can offer.
/// <para>
/// The line runs between a statement about a <em>repository</em> — a fact — and
/// one about a <em>human being</em> — a judgement they cannot comment on. "This
/// repository is 80 % Go according to GitHub" may stand here; "this person can
/// do Go" may not. Whoever writes Go into the profile is the person themselves.
/// </para>
/// </remarks>
public sealed class Connection
{
    /// <summary>GitHub's own limit for user names.</summary>
    public const int MaxLoginLength = 39;

    private readonly List<Repository> _repositories;

    private Connection(
        SubjectId subject,
        string? login,
        string nonce,
        DateTimeOffset? verifiedAt,
        DateTimeOffset? fetchedAt,
        List<Repository> repositories,
        bool languagesComplete)
    {
        Subject = subject;
        Login = login;
        Nonce = nonce;
        VerifiedAt = verifiedAt;
        FetchedAt = fetchedAt;
        _repositories = repositories;
        LanguagesComplete = languagesComplete;
    }

    /// <summary>Whose connection. It <em>is</em> the key.</summary>
    public SubjectId Subject { get; }

    /// <summary>The GitHub user name, or <c>null</c>: not named yet.</summary>
    /// <remarks>
    /// <strong><c>null</c> is a state, not a gap.</strong> Over GitHub's own
    /// sign-in the account is not named but <em>reported</em>: GitHub names only
    /// the account that really consented. Until the answer is there, this
    /// connection does not know its name — and asking for it beforehand would be
    /// a question only the gist needs an answer to.
    /// </remarks>
    public string? Login { get; private set; }

    /// <summary>The string that has to stand in the public gist.</summary>
    /// <remarks>
    /// <strong>No secret</strong>, but a one-time string. It proves only that
    /// somebody with access to the account wrote it there.
    /// </remarks>
    public string Nonce { get; private set; }

    /// <summary><c>null</c> for as long as it is not proved.</summary>
    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>When the snapshot was last fetched.</summary>
    public DateTimeOffset? FetchedAt { get; private set; }

    /// <summary>The snapshot, newest first.</summary>
    public IReadOnlyList<Repository> Repositories => _repositories;

    /// <summary>Could the languages be fetched for every repository?</summary>
    /// <remarks>
    /// <c>false</c> means: at some of them only the main language stands. The
    /// interface has to say so — see <see cref="Snapshot" />.
    /// </remarks>
    public bool LanguagesComplete { get; private set; } = true;

    /// <summary>Is the connection proved?</summary>
    public bool IsVerified => VerifiedAt is not null;

    /// <summary>The description that has to stand in the public gist.</summary>
    /// <remarks>
    /// In the <em>description</em>, not in the content: the gist listing carries
    /// it along, a content would need one fetch per gist. At sixty requests an
    /// hour without a token that is no detail.
    /// </remarks>
    public static string GistDescription(string nonce) =>
        $"workertransfer-verify-{nonce}";

    /// <summary>Names a user name and rolls the one-time string.</summary>
    /// <exception cref="LoginError">The user name is no good.</exception>
    public static Connection Open(SubjectId subject, string login) =>
        new(subject, Checked(login), NewNonce(), null, null, [], true);

    /// <summary>Opens a connection whose account GitHub itself will name.</summary>
    /// <remarks>
    /// The path over GitHub's sign-in. Here <strong>nothing is asserted</strong>:
    /// there is no named account, so there is nothing to compare — and
    /// therefore no chance of locking oneself out over a wrong name. The
    /// one-time string comes into being immediately all the same, because it is
    /// the sign-in's <c>state</c>: it proves the return belongs to this request.
    /// </remarks>
    public static Connection Await(SubjectId subject) =>
        new(subject, null, NewNonce(), null, null, [], true);

    /// <summary>The connection as a row holds it.</summary>
    public static Connection Restore(
        SubjectId subject,
        string? login,
        string nonce,
        DateTimeOffset? verifiedAt,
        DateTimeOffset? fetchedAt,
        IReadOnlyList<Repository> repositories,
        bool languagesComplete = true) =>
        new(subject, login, nonce, verifiedAt, fetchedAt,
            [.. repositories], languagesComplete);

    /// <summary>Naming a different account — the proof falls away with it.</summary>
    /// <remarks>
    /// Without this reset somebody could prove an account and afterwards change
    /// the name to a foreign one: the proof would still stand but would have
    /// been given for a different account.
    /// </remarks>
    /// <exception cref="LoginError">The user name is no good.</exception>
    public void RenameTo(string login)
    {
        var renamed = Checked(login);

        if (Login is not null && string.Equals(renamed, Login, StringComparison.OrdinalIgnoreCase))
        {
            // Only the spelling changed — that is not a different account, and
            // a proof already given still holds.
            Login = renamed;
            return;
        }

        Login = renamed;
        Nonce = NewNonce();
        VerifiedAt = null;
        FetchedAt = null;
        _repositories.Clear();
        LanguagesComplete = true;
    }

    /// <summary>Writes in the name GitHub reported.</summary>
    /// <remarks>
    /// Only for a connection that carries none yet. Were one already named, it
    /// would have to be <em>compared</em> and not overwritten — otherwise
    /// somebody names <c>torvalds</c>, authorises themselves and would get the
    /// proof silently rewritten onto their own account.
    /// </remarks>
    /// <exception cref="AlreadyNamed">An account already stands on it.</exception>
    /// <exception cref="LoginError">The user name is no good.</exception>
    public void NameOnce(string login)
    {
        if (Login is not null)
        {
            throw new AlreadyNamed();
        }

        Login = Checked(login);
    }

    /// <summary>Records that the proof has been given.</summary>
    /// <exception cref="AlreadyVerified">It had already been given.</exception>
    public void MarkVerified(DateTimeOffset now)
    {
        if (IsVerified)
        {
            throw new AlreadyVerified();
        }

        VerifiedAt = now;
    }

    /// <summary>Stores the snapshot. Only for a verified connection.</summary>
    /// <remarks>
    /// Without this check somebody could enter a foreign user name and show
    /// that person's work as their own — without ever having had access to the
    /// account.
    /// </remarks>
    /// <exception cref="NotVerified">The connection is not proved.</exception>
    public void StoreSnapshot(Snapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!IsVerified)
        {
            throw new NotVerified();
        }

        var repositories = snapshot.Repositories;
        LanguagesComplete = snapshot.LanguagesComplete;

        // Newest first. NOT by stars: those measure visibility, not work — and
        // an ordering is already a judgement.
        _repositories.Clear();
        _repositories.AddRange(
            repositories
                .OrderByDescending(entry => entry.LastPushedAt is not null)
                .ThenByDescending(entry => entry.LastPushedAt));

        FetchedAt = now;
    }

    /// <summary>Sixteen bytes, url-safe.</summary>
    private static string NewNonce() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string Checked(string? raw)
    {
        var login = (raw ?? string.Empty).Trim().TrimStart('@');

        if (login.Length == 0)
        {
            throw new LoginError("must not be empty");
        }

        if (login.Length > MaxLoginLength)
        {
            throw new LoginError($"must not exceed {MaxLoginLength} characters");
        }

        // GitHub's rules: letters, digits and single hyphens, not at the edge.
        // Checking strictly saves a fetch that would find nothing anyway — and
        // keeps a path fragment from travelling into an address.
        if (login.StartsWith('-') || login.EndsWith('-') || login.Contains("--", StringComparison.Ordinal))
        {
            throw new LoginError("has misplaced hyphens");
        }

        return login.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? login
            : throw new LoginError("may only contain letters, digits and hyphens");
    }
}

/// <summary>Finds and stores connections.</summary>
public interface IConnectionStore
{
    /// <summary>This person's connection, or <c>null</c>.</summary>
    Task<Connection?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(Connection connection, CancellationToken cancellationToken = default);

    /// <summary>Disconnecting means erasing — the snapshot goes with it.</summary>
    Task DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
