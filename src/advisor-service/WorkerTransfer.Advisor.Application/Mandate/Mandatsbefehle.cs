using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Application.Mandates;

/// <summary>„Zeig mir mein Mandate."</summary>
public sealed record MyMandateQuery(SubjectId Wer) : IAbfrage<Mandate>;

/// <summary>Liest das eigene Mandate — ohne den Ledger zu fragen.</summary>
/// <remarks>
/// Die eigene Einwilligung zu prüfen, um sich selbst zu sehen, wäre nicht nur
/// ein überflüssiger Round-Trip: wer nichts freigegeben hat, könnte sein Mandate
/// sonst nicht mehr bearbeiten (ADR-0020 §5).
/// <para>
/// Nie <c>null</c>: „nichts gesagt" <em>ist</em> ein Zustand. Ein <c>null</c>
/// zwänge die Oberfläche, sich eine Vorgabe auszudenken — und die Gefahr ist,
/// dass sie sich die falsche ausdenkt.
/// </para>
/// </remarks>
public sealed class MyMandateHandler(IMandateStore store, TimeProvider uhr)
    : IRequestHandler<MyMandateQuery, Mandate>
{
    /// <inheritdoc />
    public async Task<Mandate> Handle(
        MyMandateQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await store.GetAsync(request.Wer, cancellationToken)
               ?? Mandate.Leeres(request.Wer, uhr.GetUtcNow());
    }
}

/// <summary>„Schreib mein Mandate."</summary>
public sealed record WriteMandateCommand(
    SubjectId Wer,
    string? Eintrittstermin,
    int? GehaltMin,
    int? GehaltMax,
    int? PensumProzent,
    IReadOnlyList<string>? Ausgeschlossen) : IBefehl<Mandate>;

/// <summary>
/// Schreibt die vier Werte — und gibt <c>profile.visibility:public</c> auf,
/// sobald ein Unternehmen ausgeschlossen wird.
/// </summary>
/// <remarks>
/// <para><strong>Das ist die Zeile, an der die Abnahme hängt.</strong> „Der
/// jetzige Arbeitgeber sieht die eigene Belegschaft nicht im Scout" wird nicht
/// durch ein zweites Tor im Scout eingelöst — das wäre eine zweite Job, an
/// der über Sichtbarkeit entschieden wird — sondern im Ledger selbst.</para>
///
/// <para>Der Ledger kennt <strong>keine Verneinung</strong>. „Sichtbar für
/// alle, aber nicht für X" ist darin nicht ausdrückbar, und ADR-0037 sagt
/// deshalb: solange das so ist, gibt es den Modus „alle" nicht. Wer einen
/// Ausschluss nennt, gibt also „für alle Unternehmen" auf. Danach ist die
/// Person nur noch für Unternehmen sichtbar, denen sie einzeln freigegeben
/// hat — und der ausgeschlossene ist keines davon.</para>
///
/// <para><strong>Der Revocation läuft über den Ledger und nicht an ihm vorbei.</strong>
/// Er wird mit dem Token der Person geschrieben; der Ledger verwaltet sich
/// strikt selbst, also kann dieser Schritt nur gelingen, wenn die Person ihn
/// selbst auslöst. Schweigt der Ledger, wird <em>nichts</em> gespeichert: ein
/// Mandate mit Ausschluss neben einem weiter öffentlichen Profile wäre die
/// Halb-fort-halb-da-Lage, in der die Zusage nur noch so aussieht.</para>
/// </remarks>
public sealed class WriteMandateHandler(
    IMandateStore store, IConsentGate tor, TimeProvider uhr)
    : IRequestHandler<WriteMandateCommand, Mandate>
{
    /// <summary>Was im Ledger als Grund steht. Eine Form, nie ein Satz über jemanden.</summary>
    public const string Widerrufsgrund = "an excluded company was named";

    /// <inheritdoc />
    /// <exception cref="Eingabefehler">Eine Angabe hält die Regel nicht ein.</exception>
    public async Task<Mandate> Handle(
        WriteMandateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Erst pruefen, dann schreiben — und die Pruefung liegt im Aggregat.
        var mandate = Mandate.Write(
            request.Wer,
            request.Eintrittstermin,
            request.GehaltMin,
            request.GehaltMax,
            request.PensumProzent,
            request.Ausgeschlossen,
            uhr.GetUtcNow());

        if (mandate.AusgeschlosseneUnternehmen.Count > 0)
        {
            // VOR dem Speichern. Schweigt der Ledger, fliegt es hier — und das
            // Mandate bleibt, wie es war. Andersherum stuende ein Ausschluss in
            // der Tabelle, waehrend das Profile weiter fuer alle sichtbar ist.
            await tor.RevokeAsync(
                [StageCapabilities.ProfilOeffentlich], Widerrufsgrund, cancellationToken);
        }

        await store.SaveAsync(mandate, cancellationToken);

        return mandate;
    }
}
