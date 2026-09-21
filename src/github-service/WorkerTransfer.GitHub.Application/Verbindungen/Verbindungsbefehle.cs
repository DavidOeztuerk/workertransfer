using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Nachrichten;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Application.Connections;

/// <summary>Wie ein Befehl an einer Connection ausgegangen ist.</summary>
public abstract record ConnectionResult
{
    private ConnectionResult()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(Connection Connection) : ConnectionResult;

    /// <summary>
    /// None Connection — oder nicht freigegeben. Von außen dasselbe.
    /// </summary>
    public sealed record None : ConnectionResult;

    /// <summary>Der Gist mit der Einmalzeichenfolge war nicht zu finden.</summary>
    /// <remarks>
    /// 422 und nicht 404: die ResumeRequest war in Ordnung, der Nachweis fehlte.
    /// </remarks>
    public sealed record NichtBewiesen : ConnectionResult;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : ConnectionResult;
}

/// <summary>Den Benutzernamen nennen und die Einmalzeichenfolge bekommen.</summary>
public sealed record ConnectCommand(SubjectId Wer, string Login)
    : IBefehl<ConnectionResult>;

/// <summary>Nachweis prüfen und im selben Zug den Abzug holen.</summary>
public sealed record ProveCommand(SubjectId Wer) : IBefehl<ConnectionResult>;

/// <summary>Ist die Anmeldung über GitHub überhaupt eingerichtet?</summary>
/// <remarks>
/// Eine Aussage über <em>diesen Server</em>, nicht über einen Menschen: sie
/// trägt nichts Persönliches und legt nichts an. Die Oberfläche braucht sie,
/// bevor jemand klickt — ein Knopf, der auf eine Adresse zeigt, die es nicht
/// gibt, wäre schlimmer als kein Knopf, und andersherum wäre die Gist-Anleitung
/// als einziger Weg eine Zumutung, wo ein Knopf genügt.
/// </remarks>
public sealed record SignInPossibleQuery : IAbfrage<bool>;

/// <summary>Wohin der Browser für die Anmeldung bei GitHub geschickt wird.</summary>
/// <remarks>
/// <strong>Ein Befehl, weil er etwas anlegt.</strong> Wer noch gar keine
/// Connection hat, bekommt hier eine ohne genanntes Konto: den Namen meldet
/// GitHub. Früher stand hier eine Abfrage, die ohne Connection <c>null</c>
/// zurückgab — und damit musste jeder <em>erst</em> ein Konto tippen, um sich
/// anmelden zu dürfen. Das war die Anforderung des Gists, nicht die der
/// Anmeldung: der Gist muss wissen, in wessen Gists er sucht; GitHub sagt es
/// von selbst.
/// <para>
/// Weil er anlegt, läuft er durch <c>TransaktionsBehavior</c> — eine Abfrage
/// täte das nicht, und die neue Zeile hinge ohne Commit in der Luft.
/// </para>
/// </remarks>
public sealed record BeginSignInCommand(SubjectId Wer) : IBefehl<Anmeldebeginn>;

/// <summary>Das Ergebnis von <see cref="AnmeldungBeginnenBefehl" />.</summary>
/// <param name="Adresse">
/// Wohin der Browser geht — oder <c>null</c>, wenn keine Anmeldung eingerichtet
/// ist.
/// </param>
public sealed record Anmeldebeginn(Uri? Adresse);

/// <summary>Die Rückkehr von GitHub.</summary>
/// <param name="Code">Der Einmalcode aus der Adresszeile.</param>
/// <param name="Zustand">
/// Was GitHub unverändert zurückgereicht hat. Muss der Einmalzeichenfolge der
/// Connection entsprechen — sonst gehört die Antwort zu einer anderen ResumeRequest.
/// </param>
public sealed record CompleteSignInCommand(SubjectId Wer, string Code, string Zustand)
    : IBefehl<ConnectionResult>;

/// <summary>Den Abzug neu holen.</summary>
public sealed record AuffrischenBefehl(SubjectId Wer) : IBefehl<ConnectionResult>;

/// <summary>Trennen heißt löschen — der Abzug verschwindet mit.</summary>
public sealed record TrennenBefehl(SubjectId Wer) : IBefehl<bool>;

/// <summary>Die eigene Connection, auch die unbewiesene.</summary>
public sealed record MyConnectionQuery(SubjectId Wer) : IAbfrage<Connection?>;

/// <summary>Die Connection einer anderen Person — wenn sie freigegeben ist.</summary>
public sealed record VisibleConnectionQuery(SubjectId Wer) : IAbfrage<Connection?>;

/// <summary>Führt die Connection.</summary>
public sealed class ConnectionCommands(
    IConnectionStore store,
    IGitHub github,
    IGitHubSignIn anmeldung,
    IConsentGate tor,
    TimeProvider uhr) :
    IRequestHandler<ConnectCommand, ConnectionResult>,
    IRequestHandler<ProveCommand, ConnectionResult>,
    IRequestHandler<SignInPossibleQuery, bool>,
    IRequestHandler<BeginSignInCommand, Anmeldebeginn>,
    IRequestHandler<CompleteSignInCommand, ConnectionResult>,
    IRequestHandler<AuffrischenBefehl, ConnectionResult>,
    IRequestHandler<TrennenBefehl, bool>,
    IRequestHandler<MyConnectionQuery, Connection?>,
    IRequestHandler<VisibleConnectionQuery, Connection?>
{
    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        ConnectCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Hier wird NICHT bei GitHub angefragt: solange nichts bewiesen ist,
        // gibt es nichts zu holen — und ein Abruf verriete GitHub nur, dass
        // jemand nach diesem Konto gefragt hat.
        var vorhanden = await store.GetAsync(request.Wer, cancellationToken);
        Connection connection;

        try
        {
            if (vorhanden is null)
            {
                connection = Connection.Oeffne(request.Wer, request.Login);
            }
            else
            {
                vorhanden.Nenne_neu(request.Login);
                connection = vorhanden;
            }
        }
        catch (Loginfehler error)
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

        return Task.FromResult(anmeldung.Eingerichtet);
    }

    /// <inheritdoc />
    public async Task<Anmeldebeginn> Handle(
        BeginSignInCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!anmeldung.Eingerichtet)
        {
            return new Anmeldebeginn(null);
        }

        var connection = await store.GetAsync(request.Wer, cancellationToken);

        if (connection is null)
        {
            // NICHTS BEHAUPTEN. Die Connection entsteht ohne genanntes Konto —
            // den Namen meldet GitHub gleich selbst, und nur für das Konto, das
            // wirklich zugestimmt hat. Es gibt also nichts zu vergleichen und
            // damit auch keine Möglichkeit, jemandem einen fremden Nachweis
            // unterzuschieben.
            connection = Connection.Erwarte(request.Wer);
            await store.SaveAsync(connection, cancellationToken);
        }

        return new Anmeldebeginn(anmeldung.Anmeldeadresse(connection.Einmalzeichenfolge));
    }

    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        CompleteSignInCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Wer, cancellationToken);

        if (connection is null)
        {
            return new ConnectionResult.None();
        }

        // DER ZUSTAND ZUERST. Er beweist, dass diese Antwort zu unserer ResumeRequest
        // gehört — ohne die Prüfung könnte jemand einer angemeldeten Person
        // einen fremden Code unterschieben und ihr damit ein fremdes Konto
        // anhängen.
        if (!string.Equals(
                request.Zustand, connection.Einmalzeichenfolge, StringComparison.Ordinal))
        {
            return new ConnectionResult.NichtBewiesen();
        }

        if (connection.Nachgewiesen)
        {
            return new ConnectionResult.Erledigt(connection);
        }

        var angemeldet = await anmeldung.LoginNameAsync(request.Code, cancellationToken);

        if (angemeldet is null)
        {
            return new ConnectionResult.NichtBewiesen();
        }

        if (connection.Login is null)
        {
            // Nichts genannt, also nichts zu vergleichen: GitHub meldet allein
            // das Konto, das zugestimmt hat. Der gemeldete Name IST hier der
            // Nachweis.
            try
            {
                connection.Nenne_erstmalig(angemeldet);
            }
            catch (Loginfehler error)
            {
                // GitHub hat etwas gemeldet, das kein Benutzername sein kann.
                return new ConnectionResult.Eingabe(error.Message);
            }
        }
        else if (!string.Equals(angemeldet, connection.Login, StringComparison.OrdinalIgnoreCase))
        {
            // GENANNT HEISST VERGLICHEN. Wer `torvalds` eingetragen und sich
            // selbst angemeldet hat, hat über `torvalds` nichts bewiesen.
            return new ConnectionResult.NichtBewiesen();
        }

        connection.Weise_nach(uhr.GetUtcNow());
        await store.SaveAsync(connection, cancellationToken);

        return new ConnectionResult.Erledigt(connection);
    }

    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        ProveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Wer, cancellationToken);

        if (connection is null)
        {
            return new ConnectionResult.None();
        }

        if (!connection.Nachgewiesen)
        {
            // DER GIST BRAUCHT EINEN NAMEN, die Anmeldung nicht: gesucht wird
            // in den Gists eines bestimmten Kontos. Wer die Anmeldung begonnen
            // hat, trägt noch keinen — dann ist hier nichts zu prüfen.
            if (connection.Login is not { } login)
            {
                return new ConnectionResult.Eingabe("no account named yet");
            }

            // GitHubSignInSilent fliegt bewusst durch: der Endpunkt macht daraus
            // 503. Es hier auf „nicht bewiesen" abzubilden hieße, jemandem den
            // Nachweis abzusprechen, weil WIR gerade nicht fragen konnten.
            var found = await github.HasProofGistAsync(
                login, connection.Einmalzeichenfolge, cancellationToken);

            if (!found)
            {
                return new ConnectionResult.NichtBewiesen();
            }

            connection.Weise_nach(uhr.GetUtcNow());
        }

        // Zusammen mit dem Nachweis, weil eine bewiesene Connection ohne Inhalt
        // für niemanden etwas tut — und ein zweiter Knopf „jetzt auch laden"
        // nur eine Gelegenheit wäre, ihn nicht zu drücken.
        await Hole(connection, cancellationToken);

        return new ConnectionResult.Erledigt(connection);
    }

    /// <inheritdoc />
    public async Task<ConnectionResult> Handle(
        AuffrischenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Wer, cancellationToken);

        if (connection is null || !connection.Nachgewiesen)
        {
            return new ConnectionResult.None();
        }

        await Hole(connection, cancellationToken);

        return new ConnectionResult.Erledigt(connection);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(TrennenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await store.DeleteAsync(request.Wer, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public Task<Connection?> Handle(
        MyConnectionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Auch die unbewiesene: sonst sähe die Person nach dem ersten Schritt
        // gar nichts und wüsste nicht, welche Zeichenfolge in den Gist soll.
        return store.GetAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Connection?> Handle(
        VisibleConnectionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connection = await store.GetAsync(request.Wer, cancellationToken);

        // Eine unbewiesene Connection ist von außen nicht vorhanden: sie ist
        // eine Behauptung, und Behauptungen zeigt dieser Dienst nicht.
        if (connection is null || !connection.Nachgewiesen)
        {
            return null;
        }

        // ConsentGateSilent fliegt durch — der Endpunkt macht daraus 503.
        // Hier auf falsch zu gehen hieße zu behaupten, die Person habe nicht
        // eingewilligt.
        return await tor.MayBeShownAsync(request.Wer, cancellationToken)
            ? connection
            : null;
    }

    private async Task Hole(Connection connection, CancellationToken cancellationToken)
    {
        // Nachgewiesen heißt benannt — der Nachweis trägt den Namen entweder
        // ein oder vergleicht ihn. Die Prüfung steht hier trotzdem, weil eine
        // ResumeRequest ohne Namen sonst als halbe Adresse zu GitHub hinausginge und
        // dort etwas anderes bedeutete.
        if (connection.Login is not { } login)
        {
            throw new NichtNachgewiesen();
        }

        var abzug = await github.RepositoriesAsync(login, cancellationToken);

        connection.Lege_ab(abzug, uhr.GetUtcNow());

        await store.SaveAsync(connection, cancellationToken);
    }
}
