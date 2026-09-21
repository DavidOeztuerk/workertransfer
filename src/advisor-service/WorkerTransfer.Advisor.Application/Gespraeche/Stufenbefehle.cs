using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;

namespace WorkerTransfer.Advisor.Application.Gespraeche;

/// <summary>„Gib diesem Unternehmen Stufe N frei."</summary>
public sealed record StufeFreigebenBefehl(Guid Id, SubjectId Wer, Stufe Ziel) : IBefehl<Stufe>;

/// <summary>
/// Schreibt Ledger-Ereignisse — und legt keine Stufenspalte an.
/// </summary>
/// <remarks>
/// <para><strong>Das ist Entscheidung 2 aus ADR-0037 in einem Handler.</strong>
/// Eine Release erteilt die bestehenden Sichtbarkeiten für dieses eine
/// Unternehmen; danach steht die Stufe im Ledger und nirgends sonst. Was hier
/// <em>nicht</em> passiert, ist der eigentliche Punkt: es wird nichts an das
/// Gespräch geschrieben.</para>
///
/// <para><strong>Kumulativ.</strong> Wer Stufe 3 freigibt, erteilt auch 1 und
/// 2 — sonst stünde die neue Fähigkeit da, während die Stufe darunter fehlt,
/// und ein Lesen ergäbe trotzdem 0. Eine Stufe ist kein Sprung, sondern ein
/// State.</para>
///
/// <para><strong>Nur die Person, und der Ledger erzwingt es.</strong> Erteilt
/// wird mit ihrem Token; der Ledger verwaltet sich strikt selbst und antwortet
/// jedem anderen 403. Die Prüfung hier — gehört dieses Gespräch dir? — ist die
/// zweite, nicht die einzige.</para>
///
/// <para><strong>Und sie wirkt sofort.</strong> Kein Zwischenspeicher, weder
/// hier noch beim Lesen (ADR-0013): die nächste ResumeRequest des Unternehmens fragt
/// den Ledger neu.</para>
/// </remarks>
public sealed class StufeFreigebenHandler(IConversationStore store, IConsentGate tor)
    : IRequestHandler<StufeFreigebenBefehl, Stufe>
{
    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört einem anderen Menschen.</exception>
    public async Task<Stufe> Handle(
        StufeFreigebenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await Meins(store, request.Id, request.Wer, cancellationToken);

        var skills = StageCapabilities.Alle
            .Where(stage => stage <= request.Ziel)
            .SelectMany(stage => StageCapabilities.Erteilt(stage, conversation.Firma))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await tor.GrantAsync(skills, cancellationToken);

        return request.Ziel;
    }

    /// <summary>
    /// Das Gespräch dieses Menschen, oder 404.
    /// </summary>
    /// <remarks>
    /// „Gehört jemand anderem" und „gibt es nicht" antworten gleich: ein
    /// Unterschied verriete, dass dieses Gespräch existiert.
    /// </remarks>
    internal static async Task<Conversation> Meins(
        IConversationStore store, Guid id, SubjectId subject, CancellationToken cancellationToken)
    {
        var conversation = await store.GetAsync(id, cancellationToken);

        return conversation is null || conversation.Wer != subject
            ? throw new NoConversation()
            : conversation;
    }
}

/// <summary>„Nimm Stufe N und alles darüber zurück."</summary>
public sealed record RevokeStageCommand(Guid Id, SubjectId Wer, Stufe Ab) : IBefehl<Stufe>;

/// <summary>
/// Widerruft im Ledger — und der Revocation wirkt bei der nächsten ResumeRequest.
/// </summary>
/// <remarks>
/// <para><strong>Nach oben offen.</strong> Wer Stufe 1 zurücknimmt, nimmt 2 und
/// 3 mit: eine Stufe, die über einer weggefallenen stünde, wäre eine Erlaubnis
/// ohne Grundlage — der Resume sichtbar, das Profile nicht.</para>
///
/// <para><strong>Das Gespräch bleibt stehen.</strong> Es fällt nur aus der
/// Liste des Unternehmens (Stufe 0) und antwortet dort 404. Die Person behält
/// es in ihrer eigenen Liste und kann wieder freigeben — ein Revocation, der die
/// Zeile mit wegnähme, wäre eine Einbahnstraße.</para>
///
/// <para><strong>Der Grund ist eine Form.</strong> Der Ledger verlangt einen,
/// weil ein Revocation erklärbar sein muss; was dieser Dienst schreibt, ist
/// „stage withdrawn" und nie ein Satz über einen Menschen. Freitext schriebe
/// eine Person über sich selbst — und das ist etwas, das sie tippt, nicht etwas,
/// das ein Dienst für sie erfindet.</para>
/// </remarks>
public sealed class RevokeStageHandler(IConversationStore store, IConsentGate tor)
    : IRequestHandler<RevokeStageCommand, Stufe>
{
    /// <summary>Was im Ledger als Grund steht.</summary>
    public const string Grund = "stage withdrawn";

    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Gibt es nicht, oder gehört einem anderen Menschen.</exception>
    public async Task<Stufe> Handle(
        RevokeStageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await StufeFreigebenHandler.Meins(
            store, request.Id, request.Wer, cancellationToken);

        var skills = StageCapabilities.Alle
            .Where(stage => stage >= request.Ab)
            .SelectMany(stage => StageCapabilities.Erteilt(stage, conversation.Firma))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await tor.RevokeAsync(skills, Grund, cancellationToken);

        // Was danach noch steht, sagt der Ledger — nicht diese Rechnung. Eine
        // hier ausgerechnete Stufe waere die Spalte, nur fluechtig.
        var stages = await tor.StagesAsync([(conversation.Wer, conversation.Firma)], cancellationToken);

        return stages[0];
    }
}
