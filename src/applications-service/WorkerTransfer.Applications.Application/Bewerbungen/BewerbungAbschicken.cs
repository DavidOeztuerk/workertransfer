using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Outbox;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Bewerbungen;

namespace WorkerTransfer.Applications.Application.Bewerbungen;

/// <summary>Bewerben — und damit die Daten für dieses eine Unternehmen freigeben.</summary>
public sealed record BewerbungAbschickenBefehl(
    Guid Stelle,
    SubjectId Wer,
    string Nachricht,
    bool TeiltLebenslauf,
    bool TeiltPortfolio) : IBefehl<Bewerbungsergebnis>;

/// <summary>Legt den Vorgang an und erteilt die Freigabe dazu.</summary>
/// <remarks>
/// Die Reihenfolge ist die eigentliche Entscheidung: erst der Ledger, dann der
/// Vorgang, dann der Commit. Schlägt der Ledger fehl, fliegt
/// <see cref="EinwilligungSchweigt"/> durch die Transaktion und es wird nichts
/// committet — es gäbe sonst eine Bewerbung, die das Unternehmen nicht lesen
/// darf. Gelingt der Ledger und scheitert der Commit, hätte das Unternehmen
/// Zugriff ohne sichtbare Bewerbung; deshalb widerruft der Rückzug
/// bedingungslos, und jeder Ausgang führt in einen sauberen Zustand.
/// <para>
/// Die Freigabe entsteht im Ledger, nicht in dieser Datenbank, und sie nennt
/// den Empfänger. Es ist derselbe Mechanismus wie beim Lebenslauf, nur
/// ausgelöst durch eine andere Handlung.
/// </para>
/// </remarks>
public sealed class BewerbungAbschickenHandler(
    IBewerbungsspeicher speicher,
    IStellenauskunft stellen,
    IEinwilligungsschreiber ledger,
    IOutbox outbox,
    IUnternehmensmitgliederAbfrage mitglieder,
    IKontaktauskunft kontakt,
    TimeProvider uhr) : IRequestHandler<BewerbungAbschickenBefehl, Bewerbungsergebnis>
{
    /// <inheritdoc />
    public async Task<Bewerbungsergebnis> Handle(
        BewerbungAbschickenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stelle = await stellen.HoleAsync(request.Stelle, cancellationToken);

        if (stelle is null)
        {
            return new Bewerbungsergebnis.KeineStelle();
        }

        // Mitglieder ZUERST: scheitert identity, darf der Ledger nichts
        // erteilen. Die Suche ist nicht die Mail (ADR-0025); eine Freigabe
        // ohne Vorgang wäre die Lücke, die der Rückzug sonst räumen müsste.
        var firmenMitglieder = await mitglieder.HoleAsync(stelle.Firma, cancellationToken);

        var jetzt = uhr.GetUtcNow();
        var mitgeschickt = new Mitgeschicktes(request.TeiltLebenslauf, request.TeiltPortfolio);
        var briefkopf = await kontakt.HoleKontaktAsync(cancellationToken);
        var vorhanden = await speicher.HoleAsync(stelle.Id, request.Wer, cancellationToken);

        Bewerbung bewerbung;

        try
        {
            if (vorhanden is null)
            {
                bewerbung = Bewerbung.Schicke_ab(
                    stelle.Id,
                    // Einmal aus der Stelle übernommen und danach nicht mehr
                    // angefasst — eine Stelle wechselt nicht das Unternehmen.
                    stelle.Firma,
                    request.Wer,
                    request.Nachricht,
                    mitgeschickt,
                    jetzt,
                    briefkopf);
            }
            else
            {
                vorhanden.Schicke_erneut(request.Nachricht, mitgeschickt, jetzt, briefkopf);
                bewerbung = vorhanden;
            }
        }
        catch (UebergangNichtErlaubt fehler)
        {
            return new Bewerbungsergebnis.Zustandskonflikt(fehler.Message);
        }
        catch (Eingabefehler fehler)
        {
            return new Bewerbungsergebnis.Eingabe(fehler.Message);
        }

        await ledger.ErteileAsync(
            request.Wer,
            Einwilligungsschluessel.Fuer(
                bewerbung.Firma, mitgeschickt.Lebenslauf, mitgeschickt.Portfolio),
            cancellationToken);

        await speicher.SichereAsync(bewerbung, cancellationToken);

        // Das Unternehmen benachrichtigen: je Mitglied eine Outbox-Zeile.
        // Der Postausgang bleibt inhaltsfrei (ADR-0025): Kennung und Art,
        // nie ein Name, nie eine Stelle, nie eine E-Mail-Adresse.
        foreach (var mitglied in firmenMitglieder)
        {
            await outbox.VermerkeAsync(mitglied, Benachrichtigungsarten.Angekommen, cancellationToken);
        }

        return new Bewerbungsergebnis.Erledigt(bewerbung);
    }
}
