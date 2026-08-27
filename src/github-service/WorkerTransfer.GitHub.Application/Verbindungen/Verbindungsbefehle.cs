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
    IEinwilligungstor tor,
    TimeProvider uhr) :
    IRequestHandler<VerbindenBefehl, Verbindungsergebnis>,
    IRequestHandler<NachweisenBefehl, Verbindungsergebnis>,
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
            // GitHubSchweigt fliegt bewusst durch: der Endpunkt macht daraus
            // 503. Es hier auf „nicht bewiesen" abzubilden hieße, jemandem den
            // Nachweis abzusprechen, weil WIR gerade nicht fragen konnten.
            var gefunden = await github.HatNachweisgistAsync(
                verbindung.Login, verbindung.Einmalzeichenfolge, cancellationToken);

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
        var repositories = await github.RepositoriesAsync(verbindung.Login, cancellationToken);

        verbindung.Lege_ab(repositories, uhr.GetUtcNow());

        await speicher.SichereAsync(verbindung, cancellationToken);
    }
}
