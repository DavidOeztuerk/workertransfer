using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Domain.Faehigkeiten;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Pruefspur;

namespace WorkerTransfer.Profile.Application.Profile;

/// <summary>Das eigene Profil anlegen oder ändern.</summary>
/// <param name="Wer">Die Person — aus dem Token, nie aus dem Rumpf.</param>
/// <remarks>
/// Ein Befehl für beides. Getrennte Befehle bräuchten einen Aufrufer, der weiß,
/// ob schon eines existiert — und die Oberfläche zeigt dieselbe Maske.
/// <para>
/// Kein Feld für Sichtbarkeit, und zwar an keiner Stelle dieses Weges: die
/// steht im Consent-Ledger (ADR-0020).
/// </para>
/// </remarks>
public sealed record ProfilSpeichernBefehl(
    SubjectId Wer,
    string Ueberschrift,
    string Text,
    string Ort,
    bool RemoteMoeglich,
    IReadOnlyList<string> Faehigkeiten,
    /// <summary>Eine Stufe, oder <c>null</c> für „nichts gesagt" (ADR-0041).</summary>
    Pendelbereitschaft? Pendelbereitschaft = null,
    Umzugsbereitschaft? Umzugsbereitschaft = null) : IBefehl<Profil>;

/// <summary>Schreibt das Profil und hält den Schreibvorgang in der Spur fest.</summary>
public sealed class ProfilSpeichernHandler(
    IProfilspeicher speicher,
    IPruefspur spur,
    TimeProvider uhr) : IRequestHandler<ProfilSpeichernBefehl, Profil>
{
    /// <inheritdoc />
    public async Task<Profil> Handle(
        ProfilSpeichernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jetzt = uhr.GetUtcNow();

        // Erst die Fähigkeiten, dann das Aggregat: beide prüfen, und wer eine
        // zu lange Fähigkeit schickt, soll das erfahren, bevor irgendetwas
        // geschrieben ist.
        var faehigkeiten = Faehigkeitenliste.Aus(request.Faehigkeiten);
        var bestehend = await speicher.HoleAsync(request.Wer, cancellationToken);

        Profil profil;

        if (bestehend is null)
        {
            profil = Profil.Lege_an(
                request.Wer, request.Ueberschrift, request.Text, request.Ort,
                request.RemoteMoeglich, faehigkeiten, jetzt,
                request.Pendelbereitschaft, request.Umzugsbereitschaft);
        }
        else
        {
            bestehend.Aendere(
                request.Ueberschrift, request.Text, request.Ort,
                request.RemoteMoeglich, faehigkeiten, jetzt,
                request.Pendelbereitschaft, request.Umzugsbereitschaft);
            profil = bestehend;
        }

        // Ohne diesen Aufruf bliebe die Änderung im Arbeitsspeicher: das
        // Aggregat kommt losgelöst aus dem Speicher.
        await speicher.SpeichereAsync(profil, cancellationToken);

        await spur.HaengeAnAsync(
            new Pruefeintrag(
                bestehend is null ? Pruefhandlung.ProfilAngelegt : Pruefhandlung.ProfilGeaendert,
                jetzt,
                akteur: request.Wer,
                betroffen: request.Wer),
            cancellationToken);

        return profil;
    }
}
