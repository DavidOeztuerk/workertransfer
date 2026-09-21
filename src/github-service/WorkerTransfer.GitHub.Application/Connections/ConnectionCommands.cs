using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Messages;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Application.Connections;

/// <summary>How a command on a connection turned out.</summary>
public abstract record ConnectionResult
{
    private ConnectionResult()
    {
    }

    /// <summary>It worked.</summary>
    public sealed record Erledigt(Connection Connection) : ConnectionResult;

    /// <summary>
    /// No connection — or not released. From outside the same thing.
    /// </summary>
    public sealed record None : ConnectionResult;

    /// <summary>The gist with the one-time string was not to be found.</summary>
    /// <remarks>
    /// 422 and not 404: the request was fine, the proof was missing.
    /// </remarks>
    public sealed record NichtBewiesen : ConnectionResult;

    /// <summary>Something about the input is wrong.</summary>
    public sealed record Eingabe(string Grund) : ConnectionResult;
}

/// <summary>Name the user name and get the one-time string.</summary>
public sealed record ConnectCommand(SubjectId Subject, string Login)
    : ICommandMessage<ConnectionResult>;

/// <summary>Check the proof and fetch the snapshot in the same move.</summary>
public sealed record ProveCommand(SubjectId Subject) : ICommandMessage<ConnectionResult>;

/// <summary>Is the sign-in over GitHub configured at all?</summary>
/// <remarks>
/// A statement about <em>this server</em>, not about a human being: it carries
/// nothing personal and creates nothing. The interface needs it before anybody
/// clicks — a button pointing at an address that does not exist would be worse
/// than no button, and the other way round the gist instructions as the only
/// path would be an imposition where a button is enough.
/// </remarks>
public sealed record SignInPossibleQuery : IQueryMessage<bool>;

/// <summary>Where the browser is sent for the sign-in at GitHub.</summary>
/// <remarks>
/// <strong>A command, because it creates something.</strong> Whoever has no
/// connection at all gets one here without a named account: GitHub reports the
/// name. There used to be a query here that returned <c>null</c> without a
/// connection — and so everybody had to type an account <em>first</em> in order
/// to be allowed to sign in. That was the gist's requirement, not the sign-in's:
/// the gist has to know whose gists it searches; GitHub says so of its own
/// accord.
/// <para>
/// Because it creates, it runs through <c>TransactionBehavior</c> — a query
/// would not, and the new row would hang in the air without a commit.
/// </para>
/// </remarks>
public sealed record StartSignInCommand(SubjectId Subject) : ICommandMessage<SignInStart>;

/// <summary>The result of <see cref="StartSignInCommand" />.</summary>
/// <param name="Url">
/// Where the browser goes — or <c>null</c> when no sign-in is configured.
/// </param>
public sealed record SignInStart(Uri? Url);

/// <summary>The return from GitHub.</summary>
/// <param name="Code">The one-time code out of the address bar.</param>
/// <param name="State">
/// What GitHub handed back unchanged. Has to match the connection's one-time
/// string — otherwise the answer belongs to a different request.
/// </param>
public sealed record CompleteSignInCommand(SubjectId Subject, string Code, string Zustand)
    : ICommandMessage<ConnectionResult>;

/// <summary>Fetch the snapshot again.</summary>
public sealed record RefreshCommand(SubjectId Subject) : ICommandMessage<ConnectionResult>;

/// <summary>Disconnecting means erasing — the snapshot goes with it.</summary>
public sealed record DisconnectCommand(SubjectId Subject) : ICommandMessage<bool>;

/// <summary>One's own connection, the unproved one too.</summary>
public sealed record MyConnectionQuery(SubjectId Subject) : IQueryMessage<Connection?>;

/// <summary>Another person's connection — if it is released.</summary>
public sealed record VisibleConnectionQuery(SubjectId Subject) : IQueryMessage<Connection?>;

/// <summary>Runs the connection.</summary>
public sealed class ConnectionCommands(
    IConnectionStore store,
    IGitHub github,
    IGitHubSignIn signIn,
    IConsentGate tor,
    TimeProvider clock) :
    IRequestHandler<ConnectCommand, ConnectionResult>,
    IRequestHandler<ProveCommand, ConnectionResult>,
    IRequestHandler<SignInPossibleQuery, bool>,
    IRequestHandler<StartSignInCommand, SignInStart>,
    IRequestHandler<CompleteSignInCommand, ConnectionResult>,
    IRequestHandler<RefreshCommand, ConnectionResult>,
    IRequestHandler<DisconnectCommand, bool>,
    IRequestHandler<MyConnectionQuery, Connection?>,
    IRequestHandler<VisibleConnectionQuery, Connection?>
{
    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        ConnectCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // GitHub is NOT asked here: for as long as nothing is proved there is
        // nothing to fetch — and a fetch would only tell GitHub that somebody
        // asked about this account.
        var existing = await store.GetAsync(request.Subject, cancellationToken);
        Connection connection;

        try
        {
            if (existing is null)
            {
                connection = Connection.Open(request.Subject, request.Login);
            }
            else
            {
                existing.RenameTo(request.Login);
                connection = existing;
            }
        }
        catch (LoginError error)
        {
            return new ConnectionResult.Eingabe(error.Message);
        }

        await store.SaveAsync(connection, cancellationToken);

        return new ConnectionResult.Erledigt(connection);
    }

    /// <inheritdoc />
    public Task<bool> Handle(
        SignInPossibleQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(signIn.IsConfigured);
    }

    /// <inheritdoc />
    public async Task<SignInStart> Handle(
        StartSignInCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!signIn.IsConfigured)
        {
            return new SignInStart(null);
        }

        var connection = await store.GetAsync(request.Subject, cancellationToken);

        if (connection is null)
        {
            // ASSERT NOTHING. The connection comes into being without a named
            // account — GitHub reports the name itself in a moment, and only for
            // the account that really consented. So there is nothing to compare
            // and therefore no way of slipping somebody a foreign proof.
            connection = Connection.Await(request.Subject);
            await store.SaveAsync(connection, cancellationToken);
        }

        return new SignInStart(signIn.AuthorizeUrl(connection.Nonce));
    }

    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        CompleteSignInCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Subject, cancellationToken);

        if (connection is null)
        {
            return new ConnectionResult.None();
        }

        // THE STATE FIRST. It proves this answer belongs to our request —
        // without the check somebody could slip a signed-in person a foreign
        // code and hang a foreign account on them.
        if (!string.Equals(
                request.Zustand, connection.Nonce, StringComparison.Ordinal))
        {
            return new ConnectionResult.NichtBewiesen();
        }

        if (connection.IsVerified)
        {
            return new ConnectionResult.Erledigt(connection);
        }

        var angemeldet = await signIn.LoginNameAsync(request.Code, cancellationToken);

        if (angemeldet is null)
        {
            return new ConnectionResult.NichtBewiesen();
        }

        if (connection.Login is null)
        {
            // Nothing named, so nothing to compare: GitHub reports only the
            // account that consented. The reported name IS the proof here.
            try
            {
                connection.NameOnce(angemeldet);
            }
            catch (LoginError error)
            {
                // GitHub reported something that cannot be a user name.
                return new ConnectionResult.Eingabe(error.Message);
            }
        }
        else if (!string.Equals(angemeldet, connection.Login, StringComparison.OrdinalIgnoreCase))
        {
            // NAMED MEANS COMPARED. Whoever entered `torvalds` and authorised
            // themselves has proved nothing about `torvalds`.
            return new ConnectionResult.NichtBewiesen();
        }

        connection.MarkVerified(clock.GetUtcNow());
        await store.SaveAsync(connection, cancellationToken);

        return new ConnectionResult.Erledigt(connection);
    }

    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        ProveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Subject, cancellationToken);

        if (connection is null)
        {
            return new ConnectionResult.None();
        }

        if (!connection.IsVerified)
        {
            // THE GIST NEEDS A NAME, the sign-in does not: the search runs in
            // the gists of one particular account. Whoever began the sign-in
            // carries none yet — then there is nothing to check here.
            if (connection.Login is not { } login)
            {
                return new ConnectionResult.Eingabe("no account named yet");
            }

            // GitHubSilent deliberately flies through: the endpoint turns
            // it into 503. Mapping it to "not proved" here would mean denying
            // somebody their proof because WE could not ask just now.
            var found = await github.HasProofGistAsync(
                login, connection.Nonce, cancellationToken);

            if (!found)
            {
                return new ConnectionResult.NichtBewiesen();
            }

            connection.MarkVerified(clock.GetUtcNow());
        }

        // Together with the proof, because a proved connection without content
        // does nothing for anybody — and a second button "now load it too"
        // would only be a chance not to press it.
        await Hole(connection, cancellationToken);

        return new ConnectionResult.Erledigt(connection);
    }

    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        RefreshCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Subject, cancellationToken);

        if (connection is null || !connection.IsVerified)
        {
            return new ConnectionResult.None();
        }

        await Hole(connection, cancellationToken);

        return new ConnectionResult.Erledigt(connection);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(DisconnectCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await store.DeleteAsync(request.Subject, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public Task<Connection?> Handle(
        MyConnectionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The unproved one too: otherwise the person would see nothing at all
        // after the first step and would not know which string goes in the gist.
        return store.GetAsync(request.Subject, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Connection?> Handle(
        VisibleConnectionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Subject, cancellationToken);

        // An unproved connection does not exist from outside: it is an
        // assertion, and this service does not show assertions.
        if (connection is null || !connection.IsVerified)
        {
            return null;
        }

        // ConsentGateSilent flies through — the endpoint turns it into 503.
        // Going to false here would mean asserting the person did not consent.
        return await tor.MayBeShownAsync(request.Subject, cancellationToken)
            ? connection
            : null;
    }

    private async Task Hole(Connection connection, CancellationToken cancellationToken)
    {
        // Verified means named — the proof either writes the name in or
        // compares it. The check stands here all the same, because a request
        // without a name would otherwise go out to GitHub as half an address and
        // mean something else there.
        if (connection.Login is not { } login)
        {
            throw new NotVerified();
        }

        var snapshot = await github.RepositoriesAsync(login, cancellationToken);

        connection.StoreSnapshot(snapshot, clock.GetUtcNow());

        await store.SaveAsync(connection, cancellationToken);
    }
}
