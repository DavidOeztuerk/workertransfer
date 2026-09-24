using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Application.Conversations;

/// <summary>
/// A conversation, as far open as it is open.
/// </summary>
/// <remarks>
/// <para><strong>The stage decides what goes along — here, once.</strong> A
/// view that carries everything and an endpoint that selects would be the same
/// decision in two places: the second one forgets it some day, and then a
/// salary stands in an answer that must not show it.</para>
///
/// <para><c>null</c> means <strong>two things at once</strong> on each of these
/// fields, and deliberately so: not released, or released and never filled in.
/// On the wire the field is then absent altogether — hidden and not present
/// must stay indistinguishable (ADR-0020 §1).</para>
/// </remarks>
/// <param name="Conversation">The relation and its state.</param>
/// <param name="Stage">What the ledger says right now. Never from a column.</param>
public sealed record ConversationView(Conversation Conversation, Stage Stage)
{
    /// <summary>From stage 1.</summary>
    public string? EntryMonth { get; init; }

    /// <summary>From stage 1.</summary>
    public int? WorkloadPercent { get; init; }

    /// <summary>From stage 2.</summary>
    public int? SalaryMin { get; init; }

    /// <summary>From stage 2.</summary>
    public int? SalaryMax { get; init; }

    /// <summary>From stage 3.</summary>
    public string? Name { get; init; }

    /// <summary>From stage 3.</summary>
    public string? Email { get; init; }

    /// <summary>Builds the view — and leaves out what this stage does not carry.</summary>
    /// <param name="conversation">The relation.</param>
    /// <param name="stage">What the ledger says.</param>
    /// <param name="mandate">The person's mandate, or <c>null</c>.</param>
    /// <param name="person">Civil name and contact — only when stage 3 stands.</param>
    public static ConversationView Build(
        Conversation conversation, Stage stage, Mandate? mandate, PersonCard? person) =>
        new(conversation, stage)
        {
            EntryMonth = stage >= Stage.Profile ? mandate?.EntryMonth : null,
            WorkloadPercent = stage >= Stage.Profile ? mandate?.WorkloadPercent : null,
            SalaryMin = stage >= Stage.Documents ? mandate?.SalaryMin : null,
            SalaryMax = stage >= Stage.Documents ? mandate?.SalaryMax : null,
            Name = stage >= Stage.Person ? NonEmpty(person?.Name) : null,
            Email = stage >= Stage.Person ? NonEmpty(person?.Email) : null
        };

    /// <summary>Empty is the same as absent — otherwise "" would be a hint.</summary>
    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
