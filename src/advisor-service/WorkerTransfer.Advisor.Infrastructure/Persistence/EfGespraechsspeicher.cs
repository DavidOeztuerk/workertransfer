using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>Die Gespräche in Postgres.</summary>
public sealed class EfConversationStore(AdvisorDbContext context) : IConversationStore
{
    /// <inheritdoc />
    public async Task<Conversation?> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Gespraeche
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        return row is null ? null : ZurDomaene(row);
    }

    /// <inheritdoc />
    public async Task<Conversation?> GetRunningAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        var laufende = ConversationStates.Laufende.Select(ConversationStates.Wort).ToArray();

        var row = await context.Gespraeche
            .Where(entry => entry.SubjectId == subject.Value
                              && entry.TenantId == company.Value
                              && laufende.Contains(entry.State))
            .OrderByDescending(entry => entry.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ZurDomaene(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        // AsTracking, weil hier geändert wird: ohne das käme die Zeile losgelöst
        // zurück, die Zuweisungen gingen ins Leere, und der Aufrufer bekäme
        // trotzdem sein 200.
        var row = await context.Gespraeche
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == conversation.Id, cancellationToken);

        if (row is null)
        {
            context.Gespraeche.Add(new ConversationRow
            {
                Id = conversation.Id,
                SubjectId = conversation.Wer.Value,
                TenantId = conversation.Firma.Value,
                State = ConversationStates.Wort(conversation.State),
                Note = conversation.Anlass,
                OpenedAt = conversation.EroeffnetAm.UtcDateTime,
                UpdatedAt = conversation.GeaendertAm.UtcDateTime
            });

            return;
        }

        row.State = ConversationStates.Wort(conversation.State);
        row.UpdatedAt = conversation.GeaendertAm.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Gespraeche
            .Where(entry => entry.SubjectId == subject.Value)
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZurDomaene)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Gespraeche
            .Where(entry => entry.TenantId == company.Value)
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZurDomaene)];
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var conversations = await context.Gespraeche
            .AsTracking()
            .Where(entry => entry.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Gespraeche.RemoveRange(conversations);

        var mandate = await context.Mandate
            .AsTracking()
            .Where(entry => entry.Id == subject.Value)
            .ToListAsync(cancellationToken);

        context.Mandate.RemoveRange(mandate);

        // Und die Vermerke ÜBER diesen Menschen. Sie tragen seine Kennung und
        // sind damit personenbezogen — eine Löschung, die nur die Gespräche
        // wegnimmt, liesse in jeder Sicherung stehen, dass es diese Person gab
        // (ADR-0027).
        var vermerke = await context.Set<OutboxRow>()
            .AsTracking()
            .Where(entry => entry.UserId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Set<OutboxRow>().RemoveRange(vermerke);

        // Immer 0. Dieser Dienst kennt keinen Aufbewahrungsfall: ein Gespräch
        // ist eine Beziehung, kein Evidence, und ein Mandate gehört der Person.
        return 0;
    }

    private static Conversation ZurDomaene(ConversationRow row) =>
        Conversation.Stelle_her(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            State(row.State),
            row.Note,
            new DateTimeOffset(row.OpenedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));

    /// <summary>Das Wort zurück in einen State.</summary>
    /// <remarks>
    /// Ein unbekanntes Wort ist ein Programmierfehler und wird laut: still auf
    /// „laufend" zu fallen hiesse, ein beendetes Gespräch wieder zu öffnen.
    /// </remarks>
    private static ConversationState State(string word) => word switch
    {
        "talking" => ConversationState.Laufend,
        "agreed" => ConversationState.Zugestimmt,
        "handed_over" => ConversationState.Uebergeben,
        "ended" => ConversationState.Beendet,
        _ => throw new InvalidOperationException($"'{word}' is not a conversation state.")
    };
}
