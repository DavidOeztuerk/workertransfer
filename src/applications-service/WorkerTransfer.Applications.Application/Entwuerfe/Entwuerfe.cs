using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Outbox;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Application.Entwuerfe;

/// <summary>Wie ein Schritt am Entwurf ausgegangen ist.</summary>
public abstract record Entwurfsergebnis
{
    private Entwurfsergebnis()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(Bewerbungsentwurf Entwurf) : Entwurfsergebnis;

    /// <summary>Gibt es nicht — oder gehört jemand anderem. Von außen dasselbe.</summary>
    public sealed record Keiner : Entwurfsergebnis;

    /// <summary>Von hier aus geht dieser Schritt nicht.</summary>
    public sealed record Zustandskonflikt(string Grund) : Entwurfsergebnis;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : Entwurfsergebnis;

    /// <summary>Es ist kein Anbieter eingerichtet — und das sagt die Oberfläche.</summary>
    public sealed record KeinAnbieter(string Grund) : Entwurfsergebnis;
}

/// <summary>Für mehrere Stellen auf einmal einen Entwurf beginnen.</summary>
/// <remarks>
/// <strong>Die Massenhandlung liegt hier und nirgends sonst.</strong> Am
/// Ausgang steht sie nicht: gesendet wird je Stück, nach je einer Freigabe
/// (ADR-0034). Hier richtet sie nichts an — es entstehen Entwürfe, die
/// niemand gesehen hat und die niemanden erreichen.
/// </remarks>
public sealed record EntwuerfeAnlegenBefehl(SubjectId Wer, IReadOnlyList<Guid> Stellen)
    : IBefehl<IReadOnlyList<Bewerbungsentwurf>>;

/// <summary>Das Modell soll jetzt schreiben.</summary>
/// <remarks>
/// Ein eigener Schritt und kein Anhängsel des Anlegens: das Schreiben dauert
/// Sekunden je Stück, und zwanzig davon in einer Anfrage wären eine Anfrage,
/// die niemand abwarten kann. Die Oberfläche ruft ihn je Entwurf und zeigt
/// dabei, wie weit sie ist.
/// </remarks>
public sealed record EntwurfSchreibenBefehl(SubjectId Wer, Guid Id) : IBefehl<Entwurfsergebnis>;

/// <summary>Die eigenen Entwürfe.</summary>
public sealed record MeineEntwuerfeAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Bewerbungsentwurf>>;

/// <summary>Ein Entwurf.</summary>
public sealed record EntwurfAbfrage(SubjectId Wer, Guid Id) : IAbfrage<Bewerbungsentwurf?>;

/// <summary>Selbst am Text ändern.</summary>
public sealed record EntwurfAendernBefehl(SubjectId Wer, Guid Id, string? Betreff, string? Text)
    : IBefehl<Entwurfsergebnis>;

/// <summary>Wählen, was mitgeht.</summary>
public sealed record BeilagenWaehlenBefehl(
    SubjectId Wer, Guid Id, bool Lebenslauf, IReadOnlyList<Guid> Unterlagen)
    : IBefehl<Entwurfsergebnis>;

/// <summary>Etwas anmerken — mit oder ohne markierte Stelle.</summary>
public sealed record AnmerkenBefehl(SubjectId Wer, Guid Id, string? Text, string? Zitat)
    : IBefehl<Entwurfsergebnis>;

/// <summary>Das Modell soll die Anmerkungen umsetzen.</summary>
public sealed record UeberarbeitenBefehl(SubjectId Wer, Guid Id) : IBefehl<Entwurfsergebnis>;

/// <summary>So, und nicht anders.</summary>
public sealed record FreigebenBefehl(SubjectId Wer, Guid Id) : IBefehl<Entwurfsergebnis>;

/// <summary>Einen Entwurf wegwerfen.</summary>
public sealed record EntwurfLoeschenBefehl(SubjectId Wer, Guid Id) : IBefehl<bool>;

/// <summary>Führt die Entwürfe.</summary>
public sealed class Entwurfsbefehle(
    IEntwurfsspeicher speicher,
    IStellenauskunft stellen,
    IAnschreiber anschreiber,
    IBewerberauskunft bewerber,
    IUnternehmensauskunft unternehmen,
    TimeProvider uhr) :
    IRequestHandler<EntwuerfeAnlegenBefehl, IReadOnlyList<Bewerbungsentwurf>>,
    IRequestHandler<EntwurfSchreibenBefehl, Entwurfsergebnis>,
    IRequestHandler<MeineEntwuerfeAbfrage, IReadOnlyList<Bewerbungsentwurf>>,
    IRequestHandler<EntwurfAbfrage, Bewerbungsentwurf?>,
    IRequestHandler<EntwurfAendernBefehl, Entwurfsergebnis>,
    IRequestHandler<BeilagenWaehlenBefehl, Entwurfsergebnis>,
    IRequestHandler<AnmerkenBefehl, Entwurfsergebnis>,
    IRequestHandler<UeberarbeitenBefehl, Entwurfsergebnis>,
    IRequestHandler<FreigebenBefehl, Entwurfsergebnis>,
    IRequestHandler<EntwurfLoeschenBefehl, bool>
{
    /// <summary>Wie viele Stellen eine Massenhandlung tragen darf.</summary>
    /// <remarks>
    /// Ohne Grenze baut ein Aufrufer mit einer Adresse beliebig viele
    /// Modellaufrufe. Zwanzig ist die Zahl, bei der eine Prüfrunde noch eine
    /// Prüfrunde ist und kein Durchklicken.
    /// </remarks>
    public const int HoechsteAufEinmal = 20;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Bewerbungsentwurf>> Handle(
        EntwuerfeAnlegenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entwuerfe = new List<Bewerbungsentwurf>();

        foreach (var kennung in request.Stellen.Distinct().Take(HoechsteAufEinmal))
        {
            // Zweimal fuer dieselbe Stelle zu entwerfen kostet einen
            // Modellaufruf und verwirrt die Liste. Der offene Entwurf kommt
            // stattdessen zurueck.
            var offener = await speicher.OffenerAsync(request.Wer, kennung, cancellationToken);

            if (offener is not null)
            {
                entwuerfe.Add(offener);
                continue;
            }

            var stelle = await stellen.HoleAsync(kennung, cancellationToken);

            if (stelle is null)
            {
                continue;
            }

            var entwurf = Bewerbungsentwurf.Beginne(
                stelle.Id, stelle.Firma, request.Wer, uhr.GetUtcNow());

            await speicher.SichereAsync(entwurf, cancellationToken);
            entwuerfe.Add(entwurf);
        }

        return entwuerfe;
    }

    /// <inheritdoc />
    public async Task<Entwurfsergebnis> Handle(
        EntwurfSchreibenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entwurf = await speicher.HoleAsync(request.Wer, request.Id, cancellationToken);

        if (entwurf is null)
        {
            return new Entwurfsergebnis.Keiner();
        }

        // OHNE ANBIETER ENTSTEHT NICHTS. Ein leerer Entwurf im Stand `Pruefen`
        // waere eine Einladung, ihn versehentlich freizugeben (ADR-0034).
        if (!anschreiber.Eingerichtet)
        {
            return new Entwurfsergebnis.KeinAnbieter(
                "Es ist kein Entwurfsanbieter eingerichtet.");
        }

        var kontext = await Kontext(entwurf, cancellationToken);

        if (kontext is null)
        {
            return new Entwurfsergebnis.Keiner();
        }

        try
        {
            var ausgabe = await anschreiber.SchreibeAsync(kontext, cancellationToken);
            var (betreff, text) = Anschreibenformat.Lies(ausgabe);

            entwurf.Nimm_text_an(betreff, text, uhr.GetUtcNow());
        }
        catch (AnschreibenNichtVerfuegbar fehler)
        {
            // Die ART wird vermerkt, nie der Inhalt — und der Entwurf bleibt
            // stehen, damit die Person es erneut versuchen kann.
            entwurf.Scheitere(fehler.Message, uhr.GetUtcNow());
            await speicher.SichereAsync(entwurf, cancellationToken);

            return new Entwurfsergebnis.KeinAnbieter(fehler.Message);
        }
        catch (Eingabefehler fehler)
        {
            return new Entwurfsergebnis.Eingabe(fehler.Message);
        }

        await speicher.SichereAsync(entwurf, cancellationToken);

        return new Entwurfsergebnis.Erledigt(entwurf);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Bewerbungsentwurf>> Handle(
        MeineEntwuerfeAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.MeineAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Bewerbungsentwurf?> Handle(
        EntwurfAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.HoleAsync(request.Wer, request.Id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Entwurfsergebnis> Handle(
        EntwurfAendernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            entwurf => entwurf.Aendere_selbst(request.Betreff, request.Text, uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Entwurfsergebnis> Handle(
        BeilagenWaehlenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            entwurf => entwurf.Waehle_beilagen(
                request.Lebenslauf, request.Unterlagen, uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Entwurfsergebnis> Handle(
        AnmerkenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            entwurf => entwurf.Merke_an(request.Text, request.Zitat, uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Entwurfsergebnis> Handle(
        UeberarbeitenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entwurf = await speicher.HoleAsync(request.Wer, request.Id, cancellationToken);

        if (entwurf is null)
        {
            return new Entwurfsergebnis.Keiner();
        }

        // KEIN AUFRUF OHNE AUFTRAG. Ohne offene Anmerkung waere das die
        // Reflexionsstufe aus ADR-0024 unter anderem Namen.
        if (!entwurf.HatOffeneAnmerkungen)
        {
            return new Entwurfsergebnis.Zustandskonflikt(
                "Es gibt keine offene Anmerkung — nichts zu überarbeiten.");
        }

        if (!anschreiber.Eingerichtet)
        {
            return new Entwurfsergebnis.KeinAnbieter(
                "Es ist kein Entwurfsanbieter eingerichtet.");
        }

        var kontext = await Kontext(entwurf, cancellationToken);

        if (kontext is null)
        {
            return new Entwurfsergebnis.Keiner();
        }

        // Woertlich, samt Zitat: die Anmerkungen sind der ganze Auftrag.
        var auftraege = entwurf.OffeneAnmerkungen
            .Select(eintrag => eintrag.Zitat.Length > 0
                ? $"Zur markierten Stelle [{eintrag.Zitat}]: {eintrag.Text}"
                : eintrag.Text)
            .ToArray();

        try
        {
            var ausgabe = await anschreiber.UeberarbeiteAsync(
                kontext, entwurf.Betreff, entwurf.Text, auftraege, cancellationToken);
            var (betreff, text) = Anschreibenformat.Lies(ausgabe);

            entwurf.Ueberarbeite(betreff, text, uhr.GetUtcNow());
        }
        catch (AnschreibenNichtVerfuegbar fehler)
        {
            return new Entwurfsergebnis.KeinAnbieter(fehler.Message);
        }
        catch (Eingabefehler fehler)
        {
            return new Entwurfsergebnis.Eingabe(fehler.Message);
        }

        await speicher.SichereAsync(entwurf, cancellationToken);

        return new Entwurfsergebnis.Erledigt(entwurf);
    }

    /// <inheritdoc />
    public Task<Entwurfsergebnis> Handle(
        FreigebenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Am_entwurf(
            request.Wer, request.Id,
            entwurf => entwurf.Gib_frei(uhr.GetUtcNow()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> Handle(
        EntwurfLoeschenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entwurf = await speicher.HoleAsync(request.Wer, request.Id, cancellationToken);

        if (entwurf is null)
        {
            return false;
        }

        await speicher.LoescheAsync(request.Wer, request.Id, cancellationToken);

        return true;
    }

    /// <summary>Ein Schritt am Entwurf, mit immer derselben Fehlerbehandlung.</summary>
    private async Task<Entwurfsergebnis> Am_entwurf(
        SubjectId wer, Guid id, Action<Bewerbungsentwurf> schritt,
        CancellationToken cancellationToken)
    {
        var entwurf = await speicher.HoleAsync(wer, id, cancellationToken);

        if (entwurf is null)
        {
            return new Entwurfsergebnis.Keiner();
        }

        try
        {
            schritt(entwurf);
        }
        catch (EntwurfsschrittNichtErlaubt fehler)
        {
            return new Entwurfsergebnis.Zustandskonflikt(fehler.Message);
        }
        catch (OffeneAnmerkungen fehler)
        {
            return new Entwurfsergebnis.Zustandskonflikt(fehler.Message);
        }
        catch (Eingabefehler fehler)
        {
            return new Entwurfsergebnis.Eingabe(fehler.Message);
        }

        await speicher.SichereAsync(entwurf, cancellationToken);

        return new Entwurfsergebnis.Erledigt(entwurf);
    }

    /// <summary>Baut den Kontext — serverseitig, aus eigenen Daten und der Anzeige.</summary>
    private async Task<Anschreibenkontext?> Kontext(
        Bewerbungsentwurf entwurf, CancellationToken cancellationToken)
    {
        var stelle = await stellen.HoleAsync(entwurf.Stelle, cancellationToken);

        if (stelle is null)
        {
            return null;
        }

        var eigen = await bewerber.HoleAsync(cancellationToken);
        var firma = await unternehmen.NameAsync(stelle.Firma, cancellationToken);

        return new Anschreibenkontext(
            stelle.Titel,
            // Der Firmenname ist hier die ANSCHRIFT, keine Aussage: man kann
            // keinen Brief schreiben, ohne zu wissen, an wen (ADR-0034).
            firma,
            stelle.Ort,
            stelle.Beschreibung,
            stelle.Faehigkeiten,
            eigen.Name,
            eigen.Ueberschrift,
            eigen.Text,
            eigen.Faehigkeiten,
            eigen.Werdegang,
            eigen.Sprache);
    }
}

/// <summary>Das Ausgabeformat des Modells, gelesen statt geraten.</summary>
/// <remarks>
/// <c>BETREFF: …\n---\n&lt;Text&gt;</c>. Robust gegen das, was Modelle
/// drumherum schreiben: Markdown-Zäune, führende Erklärungen, fehlender
/// Trennstrich. Ein Parser, der nur den Idealfall kann, macht aus einer
/// leichten Abweichung ein leeres Anschreiben.
/// </remarks>
public static class Anschreibenformat
{
    /// <summary>Betreff und Text.</summary>
    public static (string Betreff, string Text) Lies(string? ausgabe)
    {
        var ganzes = (ausgabe ?? string.Empty).Trim();
        var betreff = string.Empty;
        var text = ganzes;

        var marke = ganzes.IndexOf("BETREFF:", StringComparison.OrdinalIgnoreCase);

        if (marke >= 0)
        {
            var danach = ganzes[(marke + "BETREFF:".Length)..];
            var strich = danach.IndexOf("---", StringComparison.Ordinal);

            if (strich >= 0)
            {
                betreff = danach[..strich];
                text = danach[(strich + 3)..];
            }
            else
            {
                var umbruch = danach.IndexOf('\n');

                betreff = umbruch >= 0 ? danach[..umbruch] : danach;
                text = umbruch >= 0 ? danach[(umbruch + 1)..] : string.Empty;
            }
        }

        return (
            betreff.Trim().Trim('*', '#', ' ', '"'),
            text.Trim().Trim('`').Trim());
    }
}

/// <summary>Den freigegebenen Entwurf als Bewerbung abschicken.</summary>
/// <remarks>
/// <strong>Freigeben und Senden sind zwei Handlungen.</strong> Ein Knopf
/// „freigeben und senden" spart einen Klick und nimmt der Freigabe ihren Sinn:
/// sie ist der Moment, in dem jemand sagt „so, und nicht anders", und der
/// braucht einen eigenen (ADR-0034).
/// </remarks>
public sealed record EntwurfSendenBefehl(SubjectId Wer, Guid Id) : IBefehl<Entwurfsergebnis>;

/// <summary>Macht aus einem Entwurf eine Bewerbung.</summary>
public sealed class EntwurfSendenHandler(
    IEntwurfsspeicher entwuerfe,
    IBewerbungsspeicher bewerbungen,
    IStellenauskunft stellen,
    IEinwilligungsschreiber ledger,
    IOutbox outbox,
    TimeProvider uhr) : IRequestHandler<EntwurfSendenBefehl, Entwurfsergebnis>
{
    /// <inheritdoc />
    public async Task<Entwurfsergebnis> Handle(
        EntwurfSendenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var entwurf = await entwuerfe.HoleAsync(request.Wer, request.Id, cancellationToken);

        if (entwurf is null)
        {
            return new Entwurfsergebnis.Keiner();
        }

        if (entwurf.Stand != Entwurfsstand.Freigegeben)
        {
            return new Entwurfsergebnis.Zustandskonflikt(
                "Nur ein freigegebener Entwurf geht hinaus.");
        }

        // Die Stelle noch einmal fragen: zwischen dem Entwurf und dem Versand
        // koennen Tage liegen, und eine geschlossene Ausschreibung nimmt keine
        // Bewerbung mehr an.
        var stelle = await stellen.HoleAsync(entwurf.Stelle, cancellationToken);

        if (stelle is null)
        {
            return new Entwurfsergebnis.Zustandskonflikt(
                "Diese Stelle ist nicht mehr ausgeschrieben.");
        }

        var jetzt = uhr.GetUtcNow();
        var mitgeschickt = new Mitgeschicktes(
            entwurf.TeiltLebenslauf, Portfolio: false, entwurf.Unterlagen);

        var vorhanden = await bewerbungen.HoleAsync(stelle.Id, request.Wer, cancellationToken);
        Bewerbung bewerbung;

        try
        {
            if (vorhanden is null)
            {
                bewerbung = Bewerbung.Schicke_ab(
                    stelle.Id, stelle.Firma, request.Wer, entwurf.Text, mitgeschickt, jetzt);
            }
            else
            {
                vorhanden.Schicke_erneut(entwurf.Text, mitgeschickt, jetzt);
                bewerbung = vorhanden;
            }
        }
        catch (UebergangNichtErlaubt fehler)
        {
            return new Entwurfsergebnis.Zustandskonflikt(fehler.Message);
        }
        catch (Eingabefehler fehler)
        {
            return new Entwurfsergebnis.Eingabe(fehler.Message);
        }

        // ERST DER LEDGER, DANN DER VORGANG, DANN DER COMMIT — dieselbe
        // Reihenfolge wie beim gewoehnlichen Abschicken, und aus demselben
        // Grund: es darf keine Bewerbung geben, die das Unternehmen nicht
        // lesen darf.
        await ledger.ErteileAsync(
            request.Wer,
            Einwilligungsschluessel.Fuer(
                bewerbung.Firma,
                mitgeschickt.Lebenslauf,
                mitgeschickt.Portfolio,
                mitgeschickt.Unterlagen.Count > 0),
            cancellationToken);

        await bewerbungen.SichereAsync(bewerbung, cancellationToken);

        entwurf.Vermerke_versand(jetzt);
        await entwuerfe.SichereAsync(entwurf, cancellationToken);

        // Die Person erfaehrt, dass ihre Bewerbung heraus ist. Das Unternehmen
        // zu benachrichtigen braucht die Mitgliederliste und steht in Phase 5.
        await outbox.VermerkeAsync(
            request.Wer, Benachrichtigungsarten.Bewegt, cancellationToken);

        return new Entwurfsergebnis.Erledigt(entwurf);
    }
}
