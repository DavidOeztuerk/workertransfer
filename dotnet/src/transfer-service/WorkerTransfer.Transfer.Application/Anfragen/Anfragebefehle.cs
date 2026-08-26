using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Anfragen;

namespace WorkerTransfer.Transfer.Application.Anfragen;

/// <summary>Wie eine Anfrage ausgegangen ist.</summary>
public abstract record Anfrageergebnis
{
    private Anfrageergebnis()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(Marktanfrage Anfrage) : Anfrageergebnis;

    /// <summary>
    /// Nicht sichtbar, nicht vorhanden, nicht meins — von außen dasselbe.
    /// </summary>
    /// <remarks>
    /// Der Unterschied wäre hier besonders teuer: schon die Existenz der
    /// Aussage verrät etwas.
    /// </remarks>
    public sealed record NichtSichtbar : Anfrageergebnis;

    /// <summary>Dieses Unternehmen hat schon gefragt.</summary>
    public sealed record SchonGefragt : Anfrageergebnis;

    /// <summary>Der Zustand passt nicht.</summary>
    public sealed record Zustandskonflikt(string Grund) : Anfrageergebnis;
}

/// <summary>„Darf ich sehen, ob du gerade zuhörst?"</summary>
/// <remarks>
/// Die leichtere der beiden Fragen — die schwerere ist der Vorgang selbst. Sie
/// zu beantworten kostet nichts: wer <c>unavailable</c> ist und freigibt, zeigt
/// genau das, und niemand wurde gestört.
/// </remarks>
public sealed record MarktstatusAnfragenBefehl(SubjectId Wer, TenantId Firma, SubjectId Frager)
    : IBefehl<Anfrageergebnis>;

/// <summary>Die Person antwortet.</summary>
public sealed record AnfrageBeantwortenBefehl(Guid Id, SubjectId Wer, bool Erteilen)
    : IBefehl<Anfrageergebnis>;

/// <summary>Die Person nimmt den Zugriff zurück.</summary>
public sealed record ZugriffWiderrufenBefehl(Guid Id, SubjectId Wer) : IBefehl<Anfrageergebnis>;

/// <summary>Die Anfragen über mich, mit dem Urteil des Ledgers daneben.</summary>
public sealed record MeineAnfragenAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Anfrageansicht>>;

/// <summary>Die Anfragen dieses Unternehmens.</summary>
/// <remarks>
/// Auch abgelehnte bleiben sichtbar. Sonst sähen „abgelehnt" und „nie gefragt"
/// gleich aus — und dann fragt jemand erneut, im guten Glauben.
/// </remarks>
public sealed record FirmenanfragenAbfrage(TenantId Firma) : IAbfrage<IReadOnlyList<Marktanfrage>>;

/// <summary>Eine Anfrage und ob der Zugriff gerade gilt.</summary>
/// <remarks>
/// <c>Aktiv</c> kommt frisch aus dem Ledger und kann vom Stand abweichen: nach
/// einem Widerruf bleibt <c>GRANTED</c> stehen, <c>Aktiv</c> fällt auf falsch.
/// Genau deshalb steht die Berechtigung nicht im Vorgang.
/// </remarks>
public sealed record Anfrageansicht(Marktanfrage Anfrage, bool? Aktiv);

/// <summary>Stellt, beantwortet und widerruft Anfragen.</summary>
public sealed class Anfragebefehle(
    IAnfragenspeicher speicher,
    IEinwilligungstor tor,
    IOutbox outbox,
    TimeProvider uhr) :
    IRequestHandler<MarktstatusAnfragenBefehl, Anfrageergebnis>,
    IRequestHandler<AnfrageBeantwortenBefehl, Anfrageergebnis>,
    IRequestHandler<ZugriffWiderrufenBefehl, Anfrageergebnis>,
    IRequestHandler<MeineAnfragenAbfrage, IReadOnlyList<Anfrageansicht>>,
    IRequestHandler<FirmenanfragenAbfrage, IReadOnlyList<Marktanfrage>>
{
    /// <inheritdoc />
    public async Task<Anfrageergebnis> Handle(
        MarktstatusAnfragenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Voraussetzung ist die PROFILfreigabe, nicht die Existenz eines
        // Marktstatus. Beides zu prüfen wäre ein Orakel: „hat schon einen
        // Marktstatus gepflegt" ist eine Information über die Person, die
        // niemand erfragen können soll — und sie wäre hier besonders
        // verräterisch.
        if (!await tor.DarfProfilSehenAsync(request.Wer, cancellationToken))
        {
            return new Anfrageergebnis.NichtSichtbar();
        }

        // Einmal fragen. Ohne diese Regel wäre eine Ablehnung wirkungslos: wer
        // dreimal fragen darf, hat kein Nein bekommen, sondern eine
        // Verzögerung. Gilt auch nach einem Widerruf — der ist eine stärkere
        // Aussage als die Ablehnung, nicht eine schwächere.
        if (await speicher.HoleAsync(request.Wer, request.Firma, cancellationToken) is not null)
        {
            return new Anfrageergebnis.SchonGefragt();
        }

        var anfrage = Marktanfrage.Oeffne(
            request.Wer, request.Firma, request.Frager, uhr.GetUtcNow());

        await speicher.SichereAsync(anfrage, cancellationToken);

        // In DERSELBEN Transaktion (ADR-0025): kommt die Anfrage durch, liegt
        // die Absicht fest; wird sie zurückgerollt, ist die Absicht auch weg.
        await outbox.VermerkeAsync(
            request.Wer, Benachrichtigungsarten.Angefragt, cancellationToken);

        return new Anfrageergebnis.Erledigt(anfrage);
    }

    /// <inheritdoc />
    public async Task<Anfrageergebnis> Handle(
        AnfrageBeantwortenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anfrage = await speicher.HoleAsync(request.Id, cancellationToken);

        // Eine fremde Anfrage-Kennung verhält sich wie eine fremde
        // Subjekt-Kennung: nicht vorhanden und nicht meins sind von außen
        // dasselbe.
        if (anfrage is null || anfrage.Wer != request.Wer)
        {
            return new Anfrageergebnis.NichtSichtbar();
        }

        var jetzt = uhr.GetUtcNow();

        try
        {
            if (request.Erteilen)
            {
                anfrage.Erteile(request.Wer, jetzt);
            }
            else
            {
                anfrage.Lehne_ab(request.Wer, jetzt);
            }
        }
        catch (SchonBeantwortet fehler)
        {
            return new Anfrageergebnis.Zustandskonflikt(fehler.Message);
        }
        catch (NichtDieGefragte)
        {
            return new Anfrageergebnis.NichtSichtbar();
        }

        // Erst der Ledger, dann der Vorgang: schlägt der Ledger fehl, fliegt
        // EinwilligungSchweigt durch und die Transaktion wird nie committet.
        //
        // Auch die Ablehnung widerruft. Gelingt der Ledger-Aufruf und scheitert
        // danach der Commit, existierte sonst eine Berechtigung ohne sichtbaren
        // Vorgang — der einzige Weg, auf dem dieses System nach außen OFFEN
        // scheitern könnte. Und hier wäre er am teuersten: die Berechtigung
        // sagt „diese Person hört zu".
        if (request.Erteilen)
        {
            await tor.ErteileAsync(anfrage.Wer, anfrage.Firma, cancellationToken);
        }
        else
        {
            await tor.WiderrufeAsync(anfrage.Wer, anfrage.Firma, cancellationToken);
        }

        await speicher.SichereAsync(anfrage, cancellationToken);

        return new Anfrageergebnis.Erledigt(anfrage);
    }

    /// <inheritdoc />
    public async Task<Anfrageergebnis> Handle(
        ZugriffWiderrufenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anfrage = await speicher.HoleAsync(request.Id, cancellationToken);

        if (anfrage is null || anfrage.Wer != request.Wer)
        {
            return new Anfrageergebnis.NichtSichtbar();
        }

        // Der Widerruf wirkt im Ledger, nicht im Vorgang. `GRANTED` heißt
        // „wurde einmal erteilt"; diesen Stand beim Widerruf zu ändern würde
        // die Geschichte umschreiben, und ob der Zugriff gilt, sagt ohnehin nur
        // der Ledger. Ein laufender Transfer-Vorgang bleibt bestehen: er hat
        // seine eigene Tür und seine eigene Absage.
        await tor.WiderrufeAsync(anfrage.Wer, anfrage.Firma, cancellationToken);

        return new Anfrageergebnis.Erledigt(anfrage);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Anfrageansicht>> Handle(
        MeineAnfragenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var anfragen = await speicher.FuerPersonAsync(request.Wer, cancellationToken);

        // Nur für erteilte Anfragen fragen: für PENDING und DECLINED steht die
        // Antwort fest, und jede Frage kostet einen Weg hin und zurück.
        var erteilte = anfragen.Where(a => a.Stand is Anfragestand.Granted).ToList();

        var urteile = await tor.DuerfenMarktSehenAsync(
            [.. erteilte.Select(a => (a.Wer, a.Firma))], cancellationToken);

        var aktivNachId = erteilte
            .Select((anfrage, stelle) => (anfrage.Id, Aktiv: urteile[stelle]))
            .ToDictionary(paar => paar.Id, paar => paar.Aktiv);

        return
        [
            .. anfragen.Select(anfrage => new Anfrageansicht(
                anfrage,
                anfrage.Stand is Anfragestand.Granted
                    ? aktivNachId.GetValueOrDefault(anfrage.Id)
                    : false))
        ];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Marktanfrage>> Handle(
        FirmenanfragenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.FuerFirmaAsync(request.Firma, cancellationToken);
    }
}
