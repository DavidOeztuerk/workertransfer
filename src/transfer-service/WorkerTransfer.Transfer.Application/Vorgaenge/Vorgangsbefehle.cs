using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Markt;
using WorkerTransfer.Transfer.Domain.Vorgaenge;

namespace WorkerTransfer.Transfer.Application.Vorgaenge;

/// <summary>Wie ein Befehl an einem Case ausgegangen ist.</summary>
public abstract record CaseResult
{
    private CaseResult()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(Transfer.Domain.Vorgaenge.Transfer Case) : CaseResult;

    /// <summary>Nicht vorhanden ODER nicht deins — von außen dasselbe.</summary>
    public sealed record Unbekannt : CaseResult;

    /// <summary>
    /// Kein Status, keine Release, oder <c>unavailable</c> — alles dasselbe
    /// nach außen.
    /// </summary>
    /// <remarks>
    /// Sonst wäre der Endpunkt ein Orakel darüber, wer auf der Plattform ist
    /// und wer gerade zuhört.
    /// </remarks>
    public sealed record NichtAnsprechbar : CaseResult;

    /// <summary>Es läuft schon einer.</summary>
    public sealed record LaeuftSchon : CaseResult;

    /// <summary>Die Eingabe ist in Ordnung, der Zustand passt nicht.</summary>
    public sealed record StateConflict(string Grund) : CaseResult;

    /// <summary>Mit der Eingabe stimmt etwas nicht.</summary>
    public sealed record Eingabe(string Grund) : CaseResult;
}

/// <summary>Welchen Zug die Person macht.</summary>
public enum Personenzug
{
    /// <summary>Auf ein Gespräch einlassen.</summary>
    GespraechAnnehmen,

    /// <summary>Das Angebot annehmen.</summary>
    AngebotAnnehmen,

    /// <summary>Die Release des Arbeitgebers bestätigen.</summary>
    FreigabeBestaetigen,

    /// <summary>Absagen.</summary>
    Absagen
}

/// <summary>Welchen Zug das Unternehmen macht.</summary>
public enum CompanySwitch
{
    /// <summary>Abschließen.</summary>
    Abschliessen,

    /// <summary>Zurückziehen.</summary>
    Zurueckziehen
}

/// <summary>Ein Unternehmen zeigt Interesse.</summary>
public sealed record InteresseZeigenBefehl(SubjectId Wer, TenantId Firma, string Nachricht)
    : IBefehl<CaseResult>;

/// <summary>Die Person bewegt ihren Case.</summary>
public sealed record PersonenzugBefehl(Guid Id, SubjectId Wer, Personenzug Zug)
    : IBefehl<CaseResult>;

/// <summary>Das Unternehmen bewegt den Case.</summary>
public sealed record CompanySwitchCommand(Guid Id, TenantId Firma, CompanySwitch Zug)
    : IBefehl<CaseResult>;

/// <summary>Das Unternehmen macht ein Angebot.</summary>
public sealed record AngebotMachenBefehl(
    Guid Id, TenantId Firma, string Text, string? Beginn, long? GebuehrCent)
    : IBefehl<CaseResult>;

/// <summary>Die Vorgänge dieser Person.</summary>
public sealed record MyCasesQuery(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>;

/// <summary>Die Vorgänge dieses Unternehmens.</summary>
public sealed record CompanyCasesQuery(TenantId Firma)
    : IAbfrage<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>;

/// <summary>Führt die Vorgänge.</summary>
public sealed class CaseCommands(
    ITransferStore store,
    IMarketStore market,
    IConsentGate tor,
    IOutbox outbox,
    TimeProvider uhr) :
    IRequestHandler<InteresseZeigenBefehl, CaseResult>,
    IRequestHandler<PersonenzugBefehl, CaseResult>,
    IRequestHandler<CompanySwitchCommand, CaseResult>,
    IRequestHandler<AngebotMachenBefehl, CaseResult>,
    IRequestHandler<MyCasesQuery, IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>,
    IRequestHandler<CompanyCasesQuery, IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>>
{
    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        InteresseZeigenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await market.GetAsync(request.Wer, cancellationToken);

        if (status is null)
        {
            return new CaseResult.NichtAnsprechbar();
        }

        // ConsentGateSilent fliegt bewusst durch: der Endpunkt macht daraus
        // 503.
        if (!await tor.MaySeeMarketAsync(request.Wer, request.Firma, cancellationToken))
        {
            return new CaseResult.NichtAnsprechbar();
        }

        if (!status.Ansprechbar)
        {
            // Die Release erlaubt zu SEHEN, nicht zu STÖREN.
            return new CaseResult.NichtAnsprechbar();
        }

        // Ein zweiter laufender Case wäre Nachfassen an der Absage vorbei.
        if (await store.GetCurrentAsync(request.Wer, request.Firma, cancellationToken)
            is not null)
        {
            return new CaseResult.LaeuftSchon();
        }

        Transfer.Domain.Vorgaenge.Transfer caseItem;

        try
        {
            caseItem = Transfer.Domain.Vorgaenge.Transfer.Zeige_Interesse(
                request.Wer,
                request.Firma,
                // Aus dem MarketStatus kopiert, damit eine spätere Änderung die
                // Bedingungen eines laufenden Vorgangs nicht rückwirkend
                // verschiebt.
                status.Beschaeftigt,
                request.Nachricht,
                uhr.GetUtcNow());
        }
        catch (CaseError error)
        {
            return new CaseResult.Eingabe(error.Message);
        }

        await store.SaveAsync(caseItem, cancellationToken);
        await Melde(caseItem, cancellationToken);

        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        PersonenzugBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await store.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Wer != request.Wer)
        {
            return new CaseResult.Unbekannt();
        }

        var now = uhr.GetUtcNow();

        try
        {
            switch (request.Zug)
            {
                case Personenzug.GespraechAnnehmen:
                    caseItem.Nimm_Gespraech_an(request.Wer, now);
                    break;
                case Personenzug.AngebotAnnehmen:
                    caseItem.Nimm_Angebot_an(request.Wer, now);
                    break;
                case Personenzug.FreigabeBestaetigen:
                    caseItem.Bestaetige_Freigabe(request.Wer, now);
                    break;
                case Personenzug.Absagen:
                    caseItem.Sage_ab(request.Wer, now);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }
        catch (CaseError error)
        {
            return Zu(error);
        }

        await store.SaveAsync(caseItem, cancellationToken);

        // Die Züge der Person werden NICHT gemeldet: sie weiß, was sie getan
        // hat, und eine Mail an einen Firmenverteiler mit dem Namen eines
        // Menschen wäre derselbe Leck-Kanal, nur andersherum.
        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        CompanySwitchCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await store.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Firma != request.Firma)
        {
            return new CaseResult.Unbekannt();
        }

        var now = uhr.GetUtcNow();

        try
        {
            switch (request.Zug)
            {
                case CompanySwitch.Abschliessen:
                    caseItem.Schliesse_ab(now);
                    break;
                case CompanySwitch.Zurueckziehen:
                    caseItem.Ziehe_zurueck(now);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }
        catch (CaseError error)
        {
            return Zu(error);
        }

        await store.SaveAsync(caseItem, cancellationToken);
        await Melde(caseItem, cancellationToken);

        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public async Task<CaseResult> Handle(
        AngebotMachenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await store.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Firma != request.Firma)
        {
            return new CaseResult.Unbekannt();
        }

        try
        {
            caseItem.Mache_Angebot(
                request.Text, request.Beginn, request.GebuehrCent, uhr.GetUtcNow());
        }
        catch (CaseError error)
        {
            return Zu(error);
        }

        await store.SaveAsync(caseItem, cancellationToken);
        await Melde(caseItem, cancellationToken);

        return new CaseResult.Erledigt(caseItem);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>> Handle(
        MyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForPersonAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Transfer.Domain.Vorgaenge.Transfer>> Handle(
        CompanyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForCompanyAsync(request.Firma, cancellationToken);
    }

    /// <summary>Nur Züge des Unternehmens werden gemeldet.</summary>
    /// <remarks>
    /// In DERSELBEN Transaktion wie der Zug (ADR-0025). Vorher stand hier ein
    /// HTTP-Aufruf nach dem Commit, dessen Fehler geschluckt wurde — der
    /// Case blieb, die Benachrichtigung war für immer weg.
    /// </remarks>
    private Task Melde(
        Transfer.Domain.Vorgaenge.Transfer caseItem, CancellationToken cancellationToken) =>
        outbox.NoteAsync(caseItem.Wer, Benachrichtigungsarten.Bewegt, cancellationToken);

    private static CaseResult Zu(CaseError error) => error switch
    {
        // Ein fremder Case ist von außen wie keiner.
        NichtDeiner => new CaseResult.Unbekannt(),
        UebergangNichtErlaubt => new CaseResult.StateConflict(error.Message),
        _ => new CaseResult.Eingabe(error.Message)
    };
}
