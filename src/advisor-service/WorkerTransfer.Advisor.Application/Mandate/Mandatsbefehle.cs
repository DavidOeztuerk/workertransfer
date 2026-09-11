using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandate;

namespace WorkerTransfer.Advisor.Application.Mandate;

/// <summary>„Zeig mir mein Mandat."</summary>
public sealed record MeinMandatAbfrage(SubjectId Wer) : IAbfrage<Mandat>;

/// <summary>Liest das eigene Mandat — ohne den Ledger zu fragen.</summary>
/// <remarks>
/// Die eigene Einwilligung zu prüfen, um sich selbst zu sehen, wäre nicht nur
/// ein überflüssiger Round-Trip: wer nichts freigegeben hat, könnte sein Mandat
/// sonst nicht mehr bearbeiten (ADR-0020 §5).
/// <para>
/// Nie <c>null</c>: „nichts gesagt" <em>ist</em> ein Zustand. Ein <c>null</c>
/// zwänge die Oberfläche, sich eine Vorgabe auszudenken — und die Gefahr ist,
/// dass sie sich die falsche ausdenkt.
/// </para>
/// </remarks>
public sealed class MeinMandatHandler(IMandatspeicher speicher, TimeProvider uhr)
    : IRequestHandler<MeinMandatAbfrage, Mandat>
{
    /// <inheritdoc />
    public async Task<Mandat> Handle(
        MeinMandatAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await speicher.HoleAsync(request.Wer, cancellationToken)
               ?? Mandat.Leeres(request.Wer, uhr.GetUtcNow());
    }
}

/// <summary>„Schreib mein Mandat."</summary>
public sealed record MandatSchreibenBefehl(
    SubjectId Wer,
    string? Eintrittstermin,
    int? GehaltMin,
    int? GehaltMax,
    int? PensumProzent,
    IReadOnlyList<string>? Ausgeschlossen) : IBefehl<Mandat>;

/// <summary>
/// Schreibt die vier Werte — und gibt <c>profile.visibility:public</c> auf,
/// sobald ein Unternehmen ausgeschlossen wird.
/// </summary>
/// <remarks>
/// <para><strong>Das ist die Zeile, an der die Abnahme hängt.</strong> „Der
/// jetzige Arbeitgeber sieht die eigene Belegschaft nicht im Scout" wird nicht
/// durch ein zweites Tor im Scout eingelöst — das wäre eine zweite Stelle, an
/// der über Sichtbarkeit entschieden wird — sondern im Ledger selbst.</para>
///
/// <para>Der Ledger kennt <strong>keine Verneinung</strong>. „Sichtbar für
/// alle, aber nicht für X" ist darin nicht ausdrückbar, und ADR-0037 sagt
/// deshalb: solange das so ist, gibt es den Modus „alle" nicht. Wer einen
/// Ausschluss nennt, gibt also „für alle Unternehmen" auf. Danach ist die
/// Person nur noch für Unternehmen sichtbar, denen sie einzeln freigegeben
/// hat — und der ausgeschlossene ist keines davon.</para>
///
/// <para><strong>Der Widerruf läuft über den Ledger und nicht an ihm vorbei.</strong>
/// Er wird mit dem Token der Person geschrieben; der Ledger verwaltet sich
/// strikt selbst, also kann dieser Schritt nur gelingen, wenn die Person ihn
/// selbst auslöst. Schweigt der Ledger, wird <em>nichts</em> gespeichert: ein
/// Mandat mit Ausschluss neben einem weiter öffentlichen Profil wäre die
/// Halb-fort-halb-da-Lage, in der die Zusage nur noch so aussieht.</para>
/// </remarks>
public sealed class MandatSchreibenHandler(
    IMandatspeicher speicher, IEinwilligungstor tor, TimeProvider uhr)
    : IRequestHandler<MandatSchreibenBefehl, Mandat>
{
    /// <summary>Was im Ledger als Grund steht. Eine Form, nie ein Satz über jemanden.</summary>
    public const string Widerrufsgrund = "an excluded company was named";

    /// <inheritdoc />
    /// <exception cref="Eingabefehler">Eine Angabe hält die Regel nicht ein.</exception>
    public async Task<Mandat> Handle(
        MandatSchreibenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Erst pruefen, dann schreiben — und die Pruefung liegt im Aggregat.
        var mandat = Mandat.Schreibe(
            request.Wer,
            request.Eintrittstermin,
            request.GehaltMin,
            request.GehaltMax,
            request.PensumProzent,
            request.Ausgeschlossen,
            uhr.GetUtcNow());

        if (mandat.AusgeschlosseneUnternehmen.Count > 0)
        {
            // VOR dem Speichern. Schweigt der Ledger, fliegt es hier — und das
            // Mandat bleibt, wie es war. Andersherum stuende ein Ausschluss in
            // der Tabelle, waehrend das Profil weiter fuer alle sichtbar ist.
            await tor.WiderrufeAsync(
                [Stufenfaehigkeiten.ProfilOeffentlich], Widerrufsgrund, cancellationToken);
        }

        await speicher.SichereAsync(mandat, cancellationToken);

        return mandat;
    }
}
