using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;

namespace WorkerTransfer.Advisor.Application.Gespraeche;

/// <summary>„Ich bin einverstanden."</summary>
public sealed record ZustimmenBefehl(Guid Id, SubjectId Wer) : IBefehl<ConversationView>;

/// <summary>
/// Die Zustimmung der Person — und nur sie macht eine Übergabe möglich.
/// </summary>
/// <remarks>
/// Die Einigung, aus der ein Case wird, gehört dem Menschen, um den es geht.
/// Ein Gespräch, das ein Unternehmen allein in einen Transfer heben könnte,
/// wäre eine Einladung, die man nicht ausschlagen kann.
/// </remarks>
public sealed class ZustimmenHandler(
    IConversationStore store, IConsentGate tor, TimeProvider uhr)
    : IRequestHandler<ZustimmenBefehl, ConversationView>
{
    /// <inheritdoc />
    /// <remarks>
    /// <strong>Die Stufe kommt auch hier aus dem Ledger.</strong> Sie hier
    /// auszulassen — etwa als <c>Stufe.None</c>, weil dieser Schritt sie nicht
    /// ändert — hiesse, in der Antwort „nichts freigegeben" zu behaupten,
    /// unmittelbar nachdem jemand zugestimmt hat. Eine Antwort, die lügt, ist
    /// das, was später geglaubt wird.
    /// </remarks>
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört einem anderen Menschen.</exception>
    /// <exception cref="UebergangNichtErlaubt">Von diesem State aus nicht.</exception>
    public async Task<ConversationView> Handle(
        ZustimmenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await StufeFreigebenHandler.Meins(
            store, request.Id, request.Wer, cancellationToken);

        conversation.Stimme_zu(request.Wer, uhr.GetUtcNow());

        await store.SaveAsync(conversation, cancellationToken);

        var stages = await tor.StagesAsync([(conversation.Wer, conversation.Firma)], cancellationToken);

        return new ConversationView(conversation, stages[0]);
    }
}

/// <summary>„Macht einen Case daraus."</summary>
public sealed record UebergebenBefehl(Guid Id, TenantId Firma) : IBefehl<Guid>;

/// <summary>
/// Die Übergabe an transfer-service — <strong>ein Aufruf, keine zweite
/// Zustandsmaschine.</strong>
/// </summary>
/// <remarks>
/// <para>ADR-0037 Entscheidung 4: ein Gespräch lebt hier, solange es um Stufen
/// und Mandate geht; erst die Einigung wird ein Case. Der Dreieckskonsens
/// steht in transfer-service und wird <em>nicht</em> nachgebaut — ob der
/// MarketStatus diesem Unternehmen freigegeben ist, ob die Person ansprechbar
/// ist, ob eine Release des jetzigen Arbeitgebers nötig ist: alles das prüft
/// die Tür, die hier gerufen wird.</para>
///
/// <para><strong>Eine Ablehnung von dort ist keine 500.</strong> Sie ist eine
/// Aussage über die Bedingungen dieses Vorgangs, und der Endpunkt macht daraus
/// 409. Ein Schweigen dagegen ist 503: „wir wissen es nicht" ist etwas anderes
/// als „geht nicht".</para>
///
/// <para><strong>Erst schreiben, wenn es geklappt hat.</strong> Der State geht
/// nach <c>handed_over</c>, nachdem der Case wirklich angelegt ist —
/// andersherum stünde hier „übergeben" und dort nichts.</para>
/// </remarks>
public sealed class UebergebenHandler(
    IConversationStore store, ICaseHandover handover, TimeProvider uhr)
    : IRequestHandler<UebergebenBefehl, Guid>
{
    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört einem anderen Unternehmen.</exception>
    /// <exception cref="UebergangNichtErlaubt">Die Person hat noch nicht zugestimmt.</exception>
    /// <exception cref="UebergabeAbgelehnt">transfer-service lehnt ab.</exception>
    public async Task<Guid> Handle(UebergebenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await store.GetAsync(request.Id, cancellationToken);

        if (conversation is null || conversation.Firma != request.Firma)
        {
            throw new NoConversation();
        }

        if (conversation.State != ConversationState.Zugestimmt)
        {
            throw new UebergangNichtErlaubt(conversation.State, "handed over");
        }

        var caseItem = await handover.HandOverAsync(
            conversation.Wer, conversation.Anlass, cancellationToken);

        conversation.Uebergib(uhr.GetUtcNow());

        await store.SaveAsync(conversation, cancellationToken);

        return caseItem;
    }
}

/// <summary>„Schluss."</summary>
/// <param name="Wer">Gesetzt, wenn die Person beendet.</param>
/// <param name="Firma">Gesetzt, wenn das Unternehmen beendet.</param>
public sealed record BeendenBefehl(Guid Id, SubjectId? Wer, TenantId? Firma)
    : IBefehl<ConversationView>;

/// <summary>Beide Seiten dürfen aufhören, aus jedem laufenden State.</summary>
/// <remarks>
/// Ein Verfahren, aus dem man nicht aussteigen kann, ist kein Verfahren,
/// sondern eine Falle — dieselbe Zeile steht in transfer-service, und sie gilt
/// hier genauso.
/// <para>
/// Die Stufen bleiben, wo sie sind. Ein Ende ist keine Rücknahme: wer auch die
/// Sichtbarkeit zurücknehmen will, widerruft sie — und das ist ein zweiter,
/// bewusster Schritt, keine Nebenwirkung.
/// </para>
/// </remarks>
public sealed class BeendenHandler(
    IConversationStore store, IConsentGate tor, TimeProvider uhr)
    : IRequestHandler<BeendenBefehl, ConversationView>
{
    /// <inheritdoc />
    /// <remarks>
    /// Die Stufe kommt aus dem Ledger — siehe <see cref="ZustimmenHandler"/>.
    /// Ein Ende ist keine Rücknahme, und die Antwort darf nicht so tun.
    /// </remarks>
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört jemand anderem.</exception>
    /// <exception cref="UebergangNichtErlaubt">Es läuft nicht mehr.</exception>
    public async Task<ConversationView> Handle(
        BeendenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await store.GetAsync(request.Id, cancellationToken);

        var meins = conversation is not null
                    && ((request.Wer is { } subject && conversation.Wer == subject)
                        || (request.Firma is { } company && conversation.Firma == company));

        if (conversation is null || !meins)
        {
            throw new NoConversation();
        }

        conversation.Beende(uhr.GetUtcNow());

        await store.SaveAsync(conversation, cancellationToken);

        var stages = await tor.StagesAsync([(conversation.Wer, conversation.Firma)], cancellationToken);

        return new ConversationView(conversation, stages[0]);
    }
}
