using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Anfragen;

namespace WorkerTransfer.Transfer.Application.Anfragen;

/// <summary>Wie eine ResumeRequest ausgegangen ist.</summary>
public abstract record ResumeRequestResult
{
    private ResumeRequestResult()
    {
    }

    /// <summary>Hat geklappt.</summary>
    public sealed record Erledigt(MarketRequest ResumeRequest) : ResumeRequestResult;

    /// <summary>
    /// Nicht sichtbar, nicht vorhanden, nicht meins — von außen dasselbe.
    /// </summary>
    /// <remarks>
    /// Der Unterschied wäre hier besonders teuer: schon die Existenz der
    /// Aussage verrät etwas.
    /// </remarks>
    public sealed record NichtSichtbar : ResumeRequestResult;

    /// <summary>Dieses Unternehmen hat schon gefragt.</summary>
    public sealed record SchonGefragt : ResumeRequestResult;

    /// <summary>Der Zustand passt nicht.</summary>
    public sealed record StateConflict(string Grund) : ResumeRequestResult;
}

/// <summary>„Darf ich sehen, ob du gerade zuhörst?"</summary>
/// <remarks>
/// Die leichtere der beiden Fragen — die schwerere ist der Case selbst. Sie
/// zu beantworten kostet nichts: wer <c>unavailable</c> ist und freigibt, zeigt
/// genau das, und niemand wurde gestört.
/// </remarks>
public sealed record RequestMarketStatusCommand(SubjectId Wer, TenantId Firma, SubjectId Frager)
    : IBefehl<ResumeRequestResult>;

/// <summary>Die Person antwortet.</summary>
public sealed record AnswerRequestCommand(Guid Id, SubjectId Wer, bool Erteilen)
    : IBefehl<ResumeRequestResult>;

/// <summary>Die Person nimmt den Zugriff zurück.</summary>
public sealed record RevokeAccessCommand(Guid Id, SubjectId Wer) : IBefehl<ResumeRequestResult>;

/// <summary>Die Anfragen über mich, mit dem Urteil des Ledgers daneben.</summary>
public sealed record MyRequestsQuery(SubjectId Wer)
    : IAbfrage<IReadOnlyList<ResumeRequestView>>;

/// <summary>Die Anfragen dieses Unternehmens.</summary>
/// <remarks>
/// Auch abgelehnte bleiben sichtbar. Sonst sähen „abgelehnt" und „nie gefragt"
/// gleich aus — und dann fragt jemand erneut, im guten Glauben.
/// </remarks>
public sealed record CompanyRequestsQuery(TenantId Firma) : IAbfrage<IReadOnlyList<MarketRequest>>;

/// <summary>Eine ResumeRequest und ob der Zugriff gerade gilt.</summary>
/// <remarks>
/// <c>Aktiv</c> kommt frisch aus dem Ledger und kann vom State abweichen: nach
/// einem Revocation bleibt <c>GRANTED</c> stehen, <c>Aktiv</c> fällt auf falsch.
/// Genau deshalb steht die Berechtigung nicht im Case.
/// </remarks>
public sealed record ResumeRequestView(MarketRequest ResumeRequest, bool? Aktiv);

/// <summary>Stellt, beantwortet und widerruft Anfragen.</summary>
public sealed class RequestCommands(
    IRequestStore store,
    IConsentGate tor,
    IOutbox outbox,
    TimeProvider uhr) :
    IRequestHandler<RequestMarketStatusCommand, ResumeRequestResult>,
    IRequestHandler<AnswerRequestCommand, ResumeRequestResult>,
    IRequestHandler<RevokeAccessCommand, ResumeRequestResult>,
    IRequestHandler<MyRequestsQuery, IReadOnlyList<ResumeRequestView>>,
    IRequestHandler<CompanyRequestsQuery, IReadOnlyList<MarketRequest>>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RequestMarketStatusCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Voraussetzung ist die PROFILfreigabe, nicht die Existenz eines
        // MarketStatus. Beides zu prüfen wäre ein Orakel: „hat schon einen
        // MarketStatus gepflegt" ist eine Information über die Person, die
        // niemand erfragen können soll — und sie wäre hier besonders
        // verräterisch.
        if (!await tor.MaySeeProfileAsync(request.Wer, cancellationToken))
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        // Einmal fragen. Ohne diese Regel wäre eine Ablehnung wirkungslos: wer
        // dreimal fragen darf, hat kein Nein bekommen, sondern eine
        // Verzögerung. Gilt auch nach einem Revocation — der ist eine stärkere
        // Aussage als die Ablehnung, nicht eine schwächere.
        if (await store.GetAsync(request.Wer, request.Firma, cancellationToken) is not null)
        {
            return new ResumeRequestResult.SchonGefragt();
        }

        var requestRecord = MarketRequest.Oeffne(
            request.Wer, request.Firma, request.Frager, uhr.GetUtcNow());

        await store.SaveAsync(requestRecord, cancellationToken);

        // In DERSELBEN Transaktion (ADR-0025): kommt die ResumeRequest durch, liegt
        // die Absicht fest; wird sie zurückgerollt, ist die Absicht auch weg.
        await outbox.NoteAsync(
            request.Wer, Benachrichtigungsarten.Angefragt, cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }

    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        AnswerRequestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.Id, cancellationToken);

        // Eine fremde ResumeRequest-Kennung verhält sich wie eine fremde
        // Subjekt-Kennung: nicht vorhanden und nicht meins sind von außen
        // dasselbe.
        if (requestRecord is null || requestRecord.Wer != request.Wer)
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        var now = uhr.GetUtcNow();

        try
        {
            if (request.Erteilen)
            {
                requestRecord.Erteile(request.Wer, now);
            }
            else
            {
                requestRecord.Lehne_ab(request.Wer, now);
            }
        }
        catch (AlreadyAnswered error)
        {
            return new ResumeRequestResult.StateConflict(error.Message);
        }
        catch (NichtDieGefragte)
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        // Erst der Ledger, dann der Case: schlägt der Ledger fehl, fliegt
        // ConsentGateSilent durch und die Transaktion wird nie committet.
        //
        // Auch die Ablehnung widerruft. Gelingt der Ledger-Aufruf und scheitert
        // danach der Commit, existierte sonst eine Berechtigung ohne sichtbaren
        // Case — der einzige Weg, auf dem dieses System nach außen OFFEN
        // scheitern könnte. Und hier wäre er am teuersten: die Berechtigung
        // sagt „diese Person hört zu".
        if (request.Erteilen)
        {
            await tor.GrantAsync(requestRecord.Wer, requestRecord.Firma, cancellationToken);
        }
        else
        {
            await tor.RevokeAsync(requestRecord.Wer, requestRecord.Firma, cancellationToken);
        }

        await store.SaveAsync(requestRecord, cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }

    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RevokeAccessCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.Id, cancellationToken);

        if (requestRecord is null || requestRecord.Wer != request.Wer)
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        // Der Revocation wirkt im Ledger, nicht im Case. `GRANTED` heißt
        // „wurde einmal erteilt"; diesen State beim Revocation zu ändern würde
        // die Geschichte umschreiben, und ob der Zugriff gilt, sagt ohnehin nur
        // der Ledger. Ein laufender Transfer-Case bleibt bestehen: er hat
        // seine eigene Tür und seine eigene Absage.
        await tor.RevokeAsync(requestRecord.Wer, requestRecord.Firma, cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeRequestView>> Handle(
        MyRequestsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecords = await store.ForPersonAsync(request.Wer, cancellationToken);

        // Nur für erteilte Anfragen fragen: für PENDING und DECLINED steht die
        // Antwort fest, und jede Frage kostet einen Weg hin und zurück.
        var erteilte = requestRecords.Where(a => a.State is RequestState.Granted).ToList();

        var urteile = await tor.MayEachSeeMarketAsync(
            [.. erteilte.Select(a => (a.Wer, a.Firma))], cancellationToken);

        var aktivNachId = erteilte
            .Select((requestRecord, job) => (requestRecord.Id, Aktiv: urteile[job]))
            .ToDictionary(paar => paar.Id, paar => paar.Aktiv);

        return
        [
            .. requestRecords.Select(requestRecord => new ResumeRequestView(
                requestRecord,
                requestRecord.State is RequestState.Granted
                    ? aktivNachId.GetValueOrDefault(requestRecord.Id)
                    : false))
        ];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MarketRequest>> Handle(
        CompanyRequestsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForCompanyAsync(request.Firma, cancellationToken);
    }
}
