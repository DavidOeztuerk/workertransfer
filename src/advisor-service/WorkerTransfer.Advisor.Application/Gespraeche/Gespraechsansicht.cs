using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandate;

namespace WorkerTransfer.Advisor.Application.Gespraeche;

/// <summary>
/// Ein Gespräch, so weit geöffnet, wie es geöffnet ist.
/// </summary>
/// <remarks>
/// <para><strong>Die Stufe entscheidet, was mitgegeben wird — und zwar hier,
/// einmal.</strong> Eine Ansicht, die alles trägt, und ein Endpunkt, der
/// auswählt, wäre dieselbe Entscheidung an zwei Stellen: die zweite vergisst
/// sie irgendwann, und dann steht ein Gehalt in einer Antwort, die es nicht
/// zeigen darf.</para>
///
/// <para><c>null</c> heißt an jedem dieser Felder <strong>zweierlei zugleich</strong>,
/// und das ist Absicht: nicht freigegeben, oder freigegeben und nie ausgefüllt.
/// Auf dem Draht fehlt das Feld dann ganz — verborgen und nicht vorhanden
/// müssen ununterscheidbar bleiben (ADR-0020 §1).</para>
/// </remarks>
/// <param name="Gespraech">Die Beziehung und ihr Stand.</param>
/// <param name="Stufe">Was der Ledger gerade sagt. Nie aus einer Spalte.</param>
public sealed record Gespraechsansicht(Gespraech Gespraech, Stufe Stufe)
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
    /// <param name="mandat">Das Mandat der Person, oder <c>null</c>.</param>
    /// <param name="person">Klarname und Kontakt — nur, wenn Stufe 3 steht.</param>
    public static Gespraechsansicht Baue(
        Gespraech gespraech, Stufe stufe, Mandat? mandat, Ports.Personenbild? person) =>
        new(gespraech, stufe)
        {
            Eintrittstermin = stufe >= Stufe.Profil ? mandat?.Eintrittstermin : null,
            PensumProzent = stufe >= Stufe.Profil ? mandat?.PensumProzent : null,
            GehaltMin = stufe >= Stufe.Unterlagen ? mandat?.GehaltMin : null,
            GehaltMax = stufe >= Stufe.Unterlagen ? mandat?.GehaltMax : null,
            Name = stufe >= Stufe.Person ? Nichtleer(person?.Name) : null,
            Email = stufe >= Stufe.Person ? Nichtleer(person?.Email) : null
        };

    /// <summary>Leer ist dasselbe wie nicht da — sonst wäre "" ein Hinweis.</summary>
    private static string? Nichtleer(string? wert) =>
        string.IsNullOrWhiteSpace(wert) ? null : wert;
}
