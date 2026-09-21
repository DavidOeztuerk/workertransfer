using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Domain.Faehigkeiten;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Pruefspur;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Das eigene PersonProfile anlegen oder ändern.</summary>
/// <param name="Wer">Die Person — aus dem Token, nie aus dem Rumpf.</param>
/// <remarks>
/// Ein Befehl für beides. Getrennte Befehle bräuchten einen Aufrufer, der weiß,
/// ob schon eines existiert — und die Oberfläche zeigt dieselbe Maske.
/// <para>
/// Kein Feld für Sichtbarkeit, und zwar an keiner Job dieses Weges: die
/// steht im Consent-Ledger (ADR-0020).
/// </para>
/// </remarks>
public sealed record StoreProfileCommand(
    SubjectId Wer,
    string Ueberschrift,
    string Text,
    string Ort,
    bool RemoteMoeglich,
    IReadOnlyList<string> Faehigkeiten,
    /// <summary>Eine Stufe, oder <c>null</c> für „nichts gesagt" (ADR-0041).</summary>
    Pendelbereitschaft? Pendelbereitschaft = null,
    Umzugsbereitschaft? Umzugsbereitschaft = null) : IBefehl<PersonProfile>;

/// <summary>Schreibt das PersonProfile und hält den Schreibvorgang in der Spur fest.</summary>
public sealed class StoreProfileHandler(
    IProfileStore store,
    IAuditTrail spur,
    TimeProvider uhr) : IRequestHandler<StoreProfileCommand, PersonProfile>
{
    /// <inheritdoc />
    public async Task<PersonProfile> Handle(
        StoreProfileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = uhr.GetUtcNow();

        // Erst die Fähigkeiten, dann das Aggregat: beide prüfen, und wer eine
        // zu lange Fähigkeit schickt, soll das erfahren, bevor irgendetwas
        // geschrieben ist.
        var skills = SkillList.Aus(request.Faehigkeiten);
        var bestehend = await store.GetAsync(request.Wer, cancellationToken);

        PersonProfile profile;

        if (bestehend is null)
        {
            profile = PersonProfile.Lege_an(
                request.Wer, request.Ueberschrift, request.Text, request.Ort,
                request.RemoteMoeglich, skills, now,
                request.Pendelbereitschaft, request.Umzugsbereitschaft);
        }
        else
        {
            bestehend.Aendere(
                request.Ueberschrift, request.Text, request.Ort,
                request.RemoteMoeglich, skills, now,
                request.Pendelbereitschaft, request.Umzugsbereitschaft);
            profile = bestehend;
        }

        // Ohne diesen Aufruf bliebe die Änderung im Arbeitsspeicher: das
        // Aggregat kommt losgelöst aus dem Speicher.
        await store.StoreAsync(profile, cancellationToken);

        await spur.AppendOneAsync(
            new AuditEntry(
                bestehend is null ? AuditAction.ProfileCreated : AuditAction.ProfileChanged,
                now,
                akteur: request.Wer,
                betroffen: request.Wer),
            cancellationToken);

        return profile;
    }
}
