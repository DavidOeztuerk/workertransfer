using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Markt;
using WorkerTransfer.Transfer.Domain.Vorgaenge;

namespace WorkerTransfer.Transfer.Application.Vorgaenge;

/// <summary>Wie ein Befehl an einem Vorgang ausgegangen ist.</summary>
public abstract record Vorgangsergebnis
{
    private Vorgangsergebnis()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(Transfer.Domain.Vorgaenge.Transfer Vorgang) : Vorgangsergebnis;

    /// <summary>Nicht vorhanden ODER nicht deins — von außen dasselbe.</summary>
    public sealed record Unbekannt : Vorgangsergebnis;

    /// <summary>
    /// Kein Status, keine Freigabe, oder <c>unavailable</c> — alles dasselbe
    /// nach außen.
    /// </summary>
    /// <remarks>
    /// Sonst wäre der Endpunkt ein Orakel darüber, wer auf der Plattform ist
    /// und wer gerade zuhört.
    /// </remarks>
    public sealed record NichtAnsprechbar : Vorgangsergebnis;

    /// <summary>Es läuft schon einer.</summary>
    public sealed record LaeuftSchon : Vorgangsergebnis;

    /// <summary>Die Eingabe ist in Ordnung, der Zustand passt nicht.</summary>
    public sealed record Zustandskonflikt(string Grund) : Vorgangsergebnis;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : Vorgangsergebnis;
}

/// <summary>Welchen Zug die Person macht.</summary>
public enum Personenzug
{
    /// <summary>Auf ein Gespräch einlassen.</summary>
    GespraechAnnehmen,

    /// <summary>Das Angebot annehmen.</summary>
    AngebotAnnehmen,

    /// <summary>Die Freigabe des Arbeitgebers bestätigen.</summary>
    FreigabeBestaetigen,

    /// <summary>Absagen.</summary>
    Absagen
}

/// <summary>Welchen Zug das Unternehmen macht.</summary>
public enum Firmenzug
{
    /// <summary>Abschließen.</summary>
    Abschliessen,

    /// <summary>Zurückziehen.</summary>
    Zurueckziehen
}

/// <summary>Ein Unternehmen zeigt Interesse.</summary>
public sealed record InteresseZeigenBefehl(SubjectId Wer, TenantId Firma, string Nachricht)
    : IBefehl<Vorgangsergebnis>;

/// <summary>Die Person bewegt ihren Vorgang.</summary>
public sealed record PersonenzugBefehl(Guid Id, SubjectId Wer, Personenzug Zug)
    : IBefehl<Vorgangsergebnis>;

/// <summary>Das Unternehmen bewegt den Vorgang.</summary>
public sealed record FirmenzugBefehl(Guid Id, TenantId Firma, Firmenzug Zug)
    : IBefehl<Vorgangsergebnis>;

/// <summary>Das Unternehmen macht ein Angebot.</summary>
public sealed record AngebotMachenBefehl(
    Guid Id, TenantId Firma, string Text, string? Beginn, long? GebuehrCent)
    : IBefehl<Vorgangsergebnis>;

/// <summary>Die Vorgänge dieser Person.</summary>
public sealed record MeineVorgaengeAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>;

/// <summary>Die Vorgänge dieses Unternehmens.</summary>
public sealed record FirmenvorgaengeAbfrage(TenantId Firma)
    : IAbfrage<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>;

/// <summary>Führt die Vorgänge.</summary>
public sealed class Vorgangsbefehle(
    ITransferspeicher speicher,
    IMarktspeicher markt,
    IEinwilligungstor tor,
    IOutbox outbox,
    TimeProvider uhr) :
    IRequestHandler<InteresseZeigenBefehl, Vorgangsergebnis>,
    IRequestHandler<PersonenzugBefehl, Vorgangsergebnis>,
    IRequestHandler<FirmenzugBefehl, Vorgangsergebnis>,
    IRequestHandler<AngebotMachenBefehl, Vorgangsergebnis>,
    IRequestHandler<MeineVorgaengeAbfrage, IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>,
    IRequestHandler<FirmenvorgaengeAbfrage, IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>
{
    /// <inheritdoc />
    public async Task<Vorgangsergebnis> Handle(
        InteresseZeigenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await markt.HoleAsync(request.Wer, cancellationToken);

        if (status is null)
        {
            return new Vorgangsergebnis.NichtAnsprechbar();
        }

        // EinwilligungSchweigt fliegt bewusst durch: der Endpunkt macht daraus
        // 503.
        if (!await tor.DarfMarktSehenAsync(request.Wer, request.Firma, cancellationToken))
        {
            return new Vorgangsergebnis.NichtAnsprechbar();
        }

        if (!status.Ansprechbar)
        {
            // Die Freigabe erlaubt zu SEHEN, nicht zu STÖREN.
            return new Vorgangsergebnis.NichtAnsprechbar();
        }

        // Ein zweiter laufender Vorgang wäre Nachfassen an der Absage vorbei.
        if (await speicher.HoleLaufendenAsync(request.Wer, request.Firma, cancellationToken)
            is not null)
        {
            return new Vorgangsergebnis.LaeuftSchon();
        }

        Transfer.Domain.Vorgaenge.Transfer vorgang;

        try
        {
            vorgang = Transfer.Domain.Vorgaenge.Transfer.Zeige_Interesse(
                request.Wer,
                request.Firma,
                // Aus dem Marktstatus kopiert, damit eine spätere Änderung die
                // Bedingungen eines laufenden Vorgangs nicht rückwirkend
                // verschiebt.
                status.Beschaeftigt,
                request.Nachricht,
                uhr.GetUtcNow());
        }
        catch (Vorgangsfehler fehler)
        {
            return new Vorgangsergebnis.Eingabe(fehler.Message);
        }

        await speicher.SichereAsync(vorgang, cancellationToken);
        await Melde(vorgang, cancellationToken);

        return new Vorgangsergebnis.Erledigt(vorgang);
    }

    /// <inheritdoc />
    public async Task<Vorgangsergebnis> Handle(
        PersonenzugBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vorgang = await speicher.HoleAsync(request.Id, cancellationToken);

        if (vorgang is null || vorgang.Wer != request.Wer)
        {
            return new Vorgangsergebnis.Unbekannt();
        }

        var jetzt = uhr.GetUtcNow();

        try
        {
            switch (request.Zug)
            {
                case Personenzug.GespraechAnnehmen:
                    vorgang.Nimm_Gespraech_an(request.Wer, jetzt);
                    break;
                case Personenzug.AngebotAnnehmen:
                    vorgang.Nimm_Angebot_an(request.Wer, jetzt);
                    break;
                case Personenzug.FreigabeBestaetigen:
                    vorgang.Bestaetige_Freigabe(request.Wer, jetzt);
                    break;
                case Personenzug.Absagen:
                    vorgang.Sage_ab(request.Wer, jetzt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }
        catch (Vorgangsfehler fehler)
        {
            return Zu(fehler);
        }

        await speicher.SichereAsync(vorgang, cancellationToken);

        // Die Züge der Person werden NICHT gemeldet: sie weiß, was sie getan
        // hat, und eine Mail an einen Firmenverteiler mit dem Namen eines
        // Menschen wäre derselbe Leck-Kanal, nur andersherum.
        return new Vorgangsergebnis.Erledigt(vorgang);
    }

    /// <inheritdoc />
    public async Task<Vorgangsergebnis> Handle(
        FirmenzugBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vorgang = await speicher.HoleAsync(request.Id, cancellationToken);

        if (vorgang is null || vorgang.Firma != request.Firma)
        {
            return new Vorgangsergebnis.Unbekannt();
        }

        var jetzt = uhr.GetUtcNow();

        try
        {
            switch (request.Zug)
            {
                case Firmenzug.Abschliessen:
                    vorgang.Schliesse_ab(jetzt);
                    break;
                case Firmenzug.Zurueckziehen:
                    vorgang.Ziehe_zurueck(jetzt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }
        catch (Vorgangsfehler fehler)
        {
            return Zu(fehler);
        }

        await speicher.SichereAsync(vorgang, cancellationToken);
        await Melde(vorgang, cancellationToken);

        return new Vorgangsergebnis.Erledigt(vorgang);
    }

    /// <inheritdoc />
    public async Task<Vorgangsergebnis> Handle(
        AngebotMachenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vorgang = await speicher.HoleAsync(request.Id, cancellationToken);

        if (vorgang is null || vorgang.Firma != request.Firma)
        {
            return new Vorgangsergebnis.Unbekannt();
        }

        try
        {
            vorgang.Mache_Angebot(
                request.Text, request.Beginn, request.GebuehrCent, uhr.GetUtcNow());
        }
        catch (Vorgangsfehler fehler)
        {
            return Zu(fehler);
        }

        await speicher.SichereAsync(vorgang, cancellationToken);
        await Melde(vorgang, cancellationToken);

        return new Vorgangsergebnis.Erledigt(vorgang);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>> Handle(
        MeineVorgaengeAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.FuerPersonAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>> Handle(
        FirmenvorgaengeAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.FuerFirmaAsync(request.Firma, cancellationToken);
    }

    /// <summary>Nur Züge des Unternehmens werden gemeldet.</summary>
    /// <remarks>
    /// In DERSELBEN Transaktion wie der Zug (ADR-0025). Vorher stand hier ein
    /// HTTP-Aufruf nach dem Commit, dessen Fehler geschluckt wurde — der
    /// Vorgang blieb, die Benachrichtigung war für immer weg.
    /// </remarks>
    private Task Melde(
        Transfer.Domain.Vorgaenge.Transfer vorgang, CancellationToken cancellationToken) =>
        outbox.VermerkeAsync(vorgang.Wer, Benachrichtigungsarten.Bewegt, cancellationToken);

    private static Vorgangsergebnis Zu(Vorgangsfehler fehler) => fehler switch
    {
        // Ein fremder Vorgang ist von außen wie keiner.
        NichtDeiner => new Vorgangsergebnis.Unbekannt(),
        UebergangNichtErlaubt => new Vorgangsergebnis.Zustandskonflikt(fehler.Message),
        _ => new Vorgangsergebnis.Eingabe(fehler.Message)
    };
}
