using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Nachrichten;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Application.Verbindungen;

/// <summary>Wie ein Befehl an einer Verbindung ausgegangen ist.</summary>
public abstract record Verbindungsergebnis
{
    private Verbindungsergebnis()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(Verbindung Verbindung) : Verbindungsergebnis;

    /// <summary>
    /// Keine Verbindung — oder nicht freigegeben. Von außen dasselbe.
    /// </summary>
    public sealed record Keine : Verbindungsergebnis;

    /// <summary>Der Gist mit der Einmalzeichenfolge war nicht zu finden.</summary>
    /// <remarks>
    /// 422 und nicht 404: die Anfrage war in Ordnung, der Nachweis fehlte.
    /// </remarks>
    public sealed record NichtBewiesen : Verbindungsergebnis;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : Verbindungsergebnis;
}

/// <summary>Den Benutzernamen nennen und die Einmalzeichenfolge bekommen.</summary>
public sealed record VerbindenBefehl(SubjectId Wer, string Login)
    : IBefehl<Verbindungsergebnis>;

/// <summary>Nachweis prüfen und im selben Zug den Abzug holen.</summary>
public sealed record NachweisenBefehl(SubjectId Wer) : IBefehl<Verbindungsergebnis>;

/// <summary>Ist die Anmeldung über GitHub überhaupt eingerichtet?</summary>
/// <remarks>
/// Eine Aussage über <em>diesen Server</em>, nicht über einen Menschen: sie
/// trägt nichts Persönliches und legt nichts an. Die Oberfläche braucht sie,
/// bevor jemand klickt — ein Knopf, der auf eine Adresse zeigt, die es nicht
/// gibt, wäre schlimmer als kein Knopf, und andersherum wäre die Gist-Anleitung
/// als einziger Weg eine Zumutung, wo ein Knopf genügt.
/// </remarks>
public sealed record AnmeldungMoeglichAbfrage : IAbfrage<bool>;

/// <summary>Wohin der Browser für die Anmeldung bei GitHub geschickt wird.</summary>
/// <remarks>
/// <strong>Ein Befehl, weil er etwas anlegt.</strong> Wer noch gar keine
/// Verbindung hat, bekommt hier eine ohne genanntes Konto: den Namen meldet
/// GitHub. Früher stand hier eine Abfrage, die ohne Verbindung <c>null</c>
/// zurückgab — und damit musste jeder <em>erst</em> ein Konto tippen, um sich
/// anmelden zu dürfen. Das war die Anforderung des Gists, nicht die der
/// Anmeldung: der Gist muss wissen, in wessen Gists er sucht; GitHub sagt es
/// von selbst.
/// <para>
/// Weil er anlegt, läuft er durch <c>TransaktionsBehavior</c> — eine Abfrage
/// täte das nicht, und die neue Zeile hinge ohne Commit in der Luft.
/// </para>
/// </remarks>
public sealed record AnmeldungBeginnenBefehl(SubjectId Wer) : IBefehl<Anmeldebeginn>;

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
/// Verbindung entsprechen — sonst gehört die Antwort zu einer anderen Anfrage.
/// </param>
public sealed record AnmeldungAbschliessenBefehl(SubjectId Wer, string Code, string Zustand)
    : IBefehl<Verbindungsergebnis>;

/// <summary>Den Abzug neu holen.</summary>
public sealed record AuffrischenBefehl(SubjectId Wer) : IBefehl<Verbindungsergebnis>;

/// <summary>Trennen heißt löschen — der Abzug verschwindet mit.</summary>
public sealed record TrennenBefehl(SubjectId Wer) : IBefehl<bool>;

/// <summary>Die eigene Verbindung, auch die unbewiesene.</summary>
public sealed record MeineVerbindungAbfrage(SubjectId Wer) : IAbfrage<Verbindung?>;

/// <summary>Die Verbindung einer anderen Person — wenn sie freigegeben ist.</summary>
public sealed record SichtbareVerbindungAbfrage(SubjectId Wer) : IAbfrage<Verbindung?>;

/// <summary>Führt die Verbindung.</summary>
public sealed class Verbindungsbefehle(
    IVerbindungsspeicher speicher,
    IGitHub github,
    IGitHubAnmeldung anmeldung,
    IEinwilligungstor tor,
    TimeProvider uhr) :
    IRequestHandler<VerbindenBefehl, Verbindungsergebnis>,
    IRequestHandler<NachweisenBefehl, Verbindungsergebnis>,
    IRequestHandler<AnmeldungMoeglichAbfrage, bool>,
    IRequestHandler<AnmeldungBeginnenBefehl, Anmeldebeginn>,
    IRequestHandler<AnmeldungAbschliessenBefehl, Verbindungsergebnis>,
    IRequestHandler<AuffrischenBefehl, Verbindungsergebnis>,
    IRequestHandler<TrennenBefehl, bool>,
    IRequestHandler<MeineVerbindungAbfrage, Verbindung?>,
    IRequestHandler<SichtbareVerbindungAbfrage, Verbindung?>
{
    /// <inheritdoc />
    public async Task<Verbindungsergebnis> Handle(
        VerbindenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Hier wird NICHT bei GitHub angefragt: solange nichts bewiesen ist,
        // gibt es nichts zu holen — und ein Abruf verriete GitHub nur, dass
        // jemand nach diesem Konto gefragt hat.
        var vorhanden = await speicher.HoleAsync(request.Wer, cancellationToken);
        Verbindung verbindung;

        try
        {
            if (vorhanden is null)
            {
                verbindung = Verbindung.Oeffne(request.Wer, request.Login);
            }
            else
            {
                vorhanden.Nenne_neu(request.Login);
                verbindung = vorhanden;
            }
        }
        catch (Loginfehler fehler)
        {
            return new Verbindungsergebnis.Eingabe(fehler.Message);
        }

        await speicher.SichereAsync(verbindung, cancellationToken);

        return new Verbindungsergebnis.Erledigt(verbindung);
    }

    /// <inheritdoc />
    public Task<bool> Handle(
        AnmeldungMoeglichAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(anmeldung.Eingerichtet);
    }

    /// <inheritdoc />
    public async Task<Anmeldebeginn> Handle(
        AnmeldungBeginnenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!anmeldung.Eingerichtet)
        {
            return new Anmeldebeginn(null);
        }

        var verbindung = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (verbindung is null)
        {
            // NICHTS BEHAUPTEN. Die Verbindung entsteht ohne genanntes Konto —
            // den Namen meldet GitHub gleich selbst, und nur für das Konto, das
            // wirklich zugestimmt hat. Es gibt also nichts zu vergleichen und
            // damit auch keine Möglichkeit, jemandem einen fremden Nachweis
            // unterzuschieben.
            verbindung = Verbindung.Erwarte(request.Wer);
            await speicher.SichereAsync(verbindung, cancellationToken);
        }

        return new Anmeldebeginn(anmeldung.Anmeldeadresse(verbindung.Einmalzeichenfolge));
    }

    /// <inheritdoc />
    public async Task<Verbindungsergebnis> Handle(
        AnmeldungAbschliessenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verbindung = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (verbindung is null)
        {
            return new Verbindungsergebnis.Keine();
        }

        // DER ZUSTAND ZUERST. Er beweist, dass diese Antwort zu unserer Anfrage
        // gehört — ohne die Prüfung könnte jemand einer angemeldeten Person
        // einen fremden Code unterschieben und ihr damit ein fremdes Konto
        // anhängen.
        if (!string.Equals(
                request.Zustand, verbindung.Einmalzeichenfolge, StringComparison.Ordinal))
        {
            return new Verbindungsergebnis.NichtBewiesen();
        }

        if (verbindung.Nachgewiesen)
        {
            return new Verbindungsergebnis.Erledigt(verbindung);
        }

        var angemeldet = await anmeldung.AnmeldenamenAsync(request.Code, cancellationToken);

        if (angemeldet is null)
        {
            return new Verbindungsergebnis.NichtBewiesen();
        }

        if (verbindung.Login is null)
        {
            // Nichts genannt, also nichts zu vergleichen: GitHub meldet allein
            // das Konto, das zugestimmt hat. Der gemeldete Name IST hier der
            // Nachweis.
            try
            {
                verbindung.Nenne_erstmalig(angemeldet);
            }
            catch (Loginfehler fehler)
            {
                // GitHub hat etwas gemeldet, das kein Benutzername sein kann.
                return new Verbindungsergebnis.Eingabe(fehler.Message);
            }
        }
        else if (!string.Equals(angemeldet, verbindung.Login, StringComparison.OrdinalIgnoreCase))
        {
            // GENANNT HEISST VERGLICHEN. Wer `torvalds` eingetragen und sich
            // selbst angemeldet hat, hat über `torvalds` nichts bewiesen.
            return new Verbindungsergebnis.NichtBewiesen();
        }

        verbindung.Weise_nach(uhr.GetUtcNow());
        await speicher.SichereAsync(verbindung, cancellationToken);

        return new Verbindungsergebnis.Erledigt(verbindung);
    }

    /// <inheritdoc />
    public async Task<Verbindungsergebnis> Handle(
        NachweisenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verbindung = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (verbindung is null)
        {
            return new Verbindungsergebnis.Keine();
        }

        if (!verbindung.Nachgewiesen)
        {
            // DER GIST BRAUCHT EINEN NAMEN, die Anmeldung nicht: gesucht wird
            // in den Gists eines bestimmten Kontos. Wer die Anmeldung begonnen
            // hat, trägt noch keinen — dann ist hier nichts zu prüfen.
            if (verbindung.Login is not { } login)
            {
                return new Verbindungsergebnis.Eingabe("no account named yet");
            }

            // GitHubSchweigt fliegt bewusst durch: der Endpunkt macht daraus
            // 503. Es hier auf „nicht bewiesen" abzubilden hieße, jemandem den
            // Nachweis abzusprechen, weil WIR gerade nicht fragen konnten.
            var gefunden = await github.HatNachweisgistAsync(
                login, verbindung.Einmalzeichenfolge, cancellationToken);

            if (!gefunden)
            {
                return new Verbindungsergebnis.NichtBewiesen();
            }

            verbindung.Weise_nach(uhr.GetUtcNow());
        }

        // Zusammen mit dem Nachweis, weil eine bewiesene Verbindung ohne Inhalt
        // für niemanden etwas tut — und ein zweiter Knopf „jetzt auch laden"
        // nur eine Gelegenheit wäre, ihn nicht zu drücken.
        await Hole(verbindung, cancellationToken);

        return new Verbindungsergebnis.Erledigt(verbindung);
    }

    /// <inheritdoc />
    public async Task<Verbindungsergebnis> Handle(
        AuffrischenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verbindung = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (verbindung is null || !verbindung.Nachgewiesen)
        {
            return new Verbindungsergebnis.Keine();
        }

        await Hole(verbindung, cancellationToken);

        return new Verbindungsergebnis.Erledigt(verbindung);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(TrennenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await speicher.LoescheAsync(request.Wer, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public Task<Verbindung?> Handle(
        MeineVerbindungAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Auch die unbewiesene: sonst sähe die Person nach dem ersten Schritt
        // gar nichts und wüsste nicht, welche Zeichenfolge in den Gist soll.
        return speicher.HoleAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Verbindung?> Handle(
        SichtbareVerbindungAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var verbindung = await speicher.HoleAsync(request.Wer, cancellationToken);

        // Eine unbewiesene Verbindung ist von außen nicht vorhanden: sie ist
        // eine Behauptung, und Behauptungen zeigt dieser Dienst nicht.
        if (verbindung is null || !verbindung.Nachgewiesen)
        {
            return null;
        }

        // EinwilligungSchweigt fliegt durch — der Endpunkt macht daraus 503.
        // Hier auf falsch zu gehen hieße zu behaupten, die Person habe nicht
        // eingewilligt.
        return await tor.DarfGezeigtWerdenAsync(request.Wer, cancellationToken)
            ? verbindung
            : null;
    }

    private async Task Hole(Verbindung verbindung, CancellationToken cancellationToken)
    {
        // Nachgewiesen heißt benannt — der Nachweis trägt den Namen entweder
        // ein oder vergleicht ihn. Die Prüfung steht hier trotzdem, weil eine
        // Anfrage ohne Namen sonst als halbe Adresse zu GitHub hinausginge und
        // dort etwas anderes bedeutete.
        if (verbindung.Login is not { } login)
        {
            throw new NichtNachgewiesen();
        }

        var abzug = await github.RepositoriesAsync(login, cancellationToken);

        verbindung.Lege_ab(abzug, uhr.GetUtcNow());

        await speicher.SichereAsync(verbindung, cancellationToken);
    }
}
