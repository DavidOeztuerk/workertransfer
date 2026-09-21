using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Application.Gespraeche;

/// <summary>
/// Ein Gespräch, so weit geöffnet, wie es geöffnet ist.
/// </summary>
/// <remarks>
/// <para><strong>Die Stufe entscheidet, was mitgegeben wird — und zwar hier,
/// einmal.</strong> Eine Ansicht, die alles trägt, und ein Endpunkt, der
/// auswählt, wäre dieselbe Entscheidung an zwei Jobs: die zweite vergisst
/// sie irgendwann, und dann steht ein Gehalt in einer Antwort, die es nicht
/// zeigen darf.</para>
///
/// <para><c>null</c> heißt an jedem dieser Felder <strong>zweierlei zugleich</strong>,
/// und das ist Absicht: nicht freigegeben, oder freigegeben und nie ausgefüllt.
/// Auf dem Draht fehlt das Feld dann ganz — verborgen und nicht vorhanden
/// müssen ununterscheidbar bleiben (ADR-0020 §1).</para>
/// </remarks>
/// <param name="Gespraech">Die Beziehung und ihr State.</param>
/// <param name="Stufe">Was der Ledger gerade sagt. Nie aus einer Spalte.</param>
public sealed record ConversationView(Conversation Conversation, Stufe Stufe)
{
    /// <summary>Ab Stufe 1.</summary>
    public string? Eintrittstermin { get; init; }

    /// <summary>Ab Stufe 1.</summary>
    public int? PensumProzent { get; init; }

    /// <summary>Ab Stufe 2.</summary>
    public int? GehaltMin { get; init; }

    /// <summary>Ab Stufe 2.</summary>
    public int? GehaltMax { get; init; }

    /// <summary>Ab Stufe 3.</summary>
    public string? Name { get; init; }

    /// <summary>Ab Stufe 3.</summary>
    public string? Email { get; init; }

    /// <summary>Baut die Ansicht — und lässt weg, was diese Stufe nicht trägt.</summary>
    /// <param name="gespraech">Die Beziehung.</param>
    /// <param name="stufe">Was der Ledger sagt.</param>
    /// <param name="mandat">Das Mandate der Person, oder <c>null</c>.</param>
    /// <param name="person">Klarname und Kontakt — nur, wenn Stufe 3 steht.</param>
    public static ConversationView Build(
        Conversation conversation, Stufe stage, Mandate? mandate, Ports.Personenbild? person) =>
        new(conversation, stage)
        {
            Eintrittstermin = stage >= Stufe.Profile ? mandate?.Eintrittstermin : null,
            PensumProzent = stage >= Stufe.Profile ? mandate?.PensumProzent : null,
            GehaltMin = stage >= Stufe.Unterlagen ? mandate?.GehaltMin : null,
            GehaltMax = stage >= Stufe.Unterlagen ? mandate?.GehaltMax : null,
            Name = stage >= Stufe.Person ? Nichtleer(person?.Name) : null,
            Email = stage >= Stufe.Person ? Nichtleer(person?.Email) : null
        };

    /// <summary>Leer ist dasselbe wie nicht da — sonst wäre "" ein Hinweis.</summary>
    private static string? Nichtleer(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
