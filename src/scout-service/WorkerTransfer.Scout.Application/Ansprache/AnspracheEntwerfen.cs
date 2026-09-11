using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Nachrichten;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Application.Ansprache;

/// <summary>Bitte um einen Entwurf für eine erste Nachricht an einen Menschen.</summary>
/// <param name="Firma">Das Unternehmen des Aufrufers — aus dem Token.</param>
/// <param name="Wen">Wer angesprochen werden soll.</param>
/// <param name="Gesucht">Wonach das Unternehmen gesucht hat.</param>
/// <param name="Wunsch">Was der Mensch am Entwurf anders haben will.</param>
/// <remarks>
/// Eine <see cref="IAbfrage{TAntwort}"/> und ausdrücklich kein Befehl: es wird
/// nichts geschrieben, also darf auch keine Transaktion aufgehen. Der Entwurf
/// lebt im Formular, bis ein Mensch ihn abschickt — und abschicken tut er ihn
/// selbst.
/// </remarks>
public sealed record AnspracheAbfrage(
    TenantId Firma,
    SubjectId Wen,
    IReadOnlyList<string> Gesucht,
    string Wunsch) : IAbfrage<string>;

/// <summary>„Niemand zu Hause" — zu dieser Person gibt es hier nichts zu entwerfen.</summary>
/// <remarks>
/// Dieselbe Antwort für „nicht freigegeben" und „gibt es nicht", und das muss so
/// bleiben: ein Unterschied sagte, ob dieser Mensch auf der Plattform ist
/// (ADR-0020).
/// </remarks>
public sealed class KeinAnsprechpartner() : Exception("no such profile");

/// <summary>
/// Fragt zuerst den Ledger, baut dann den Kontext — und speichert nichts.
/// </summary>
/// <remarks>
/// <para><strong>Der Ledger steht vor dem Modell, nicht daneben.</strong> Ohne
/// diese Reihenfolge könnte jemand einen Entwurf über eine Person bestellen,
/// die ihm nie etwas freigegeben hat — und das Modell bekäme ihre Worte zu
/// sehen. Gefragt wird synchron und ohne Zwischenspeicher, wie überall
/// (ADR-0013).</para>
///
/// <para><strong>Der Kontext kommt aus dem GESPEICHERTEN Profil</strong>, nicht
/// aus der Anfrage: was der Aufrufer nicht schicken kann, kann er nicht in
/// einen fremden Dienst schleusen. Er steuert genau zwei Dinge bei, und beide
/// sind seine eigenen Worte — wonach er gesucht hat, und was er am Entwurf
/// anders haben will.</para>
///
/// <para><strong>Nichts wird gespeichert</strong> — nicht der Prompt, nicht die
/// Antwort, und kein Vermerk „hat einen Entwurf bestellt". Ein solcher Vermerk
/// wäre genau das Verzeichnis „wer hat sich für wen interessiert", das
/// ADR-0033 nicht entstehen lassen will.</para>
///
/// <para>Und ausdrücklich <strong>keine Benachrichtigung</strong> an die
/// angesprochene Person: sie erfährt aus der Suche, dass sie entdeckt wurde
/// (<c>profile_discovered</c>), und eine zweite Nachricht „jemand entwirft
/// gerade etwas an dich" wäre ein Zähler über die eigene Sichtbarkeit.</para>
/// </remarks>
public sealed class AnspracheHandler(
    IEinwilligungstor tor,
    IProfilsuche profile,
    IEntwerfer entwerfer) : IRequestHandler<AnspracheAbfrage, string>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    /// <exception cref="KeinAnsprechpartner">Nicht freigegeben, oder es gibt niemanden.</exception>
    /// <exception cref="AnspracheNichtVerfuegbar">Kein Anbieter, oder er schweigt.</exception>
    public async Task<string> Handle(
        AnspracheAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var urteile = await tor.DarfSehenAlleAsync(
            [request.Wen], request.Firma, cancellationToken);

        if (urteile.Count != 1)
        {
            throw new EinwilligungSchweigt($"{urteile.Count} Antworten auf eine Frage");
        }

        if (!urteile[0])
        {
            throw new KeinAnsprechpartner();
        }

        var profil = await profile.HoleAsync(request.Wen, cancellationToken)
                     ?? throw new KeinAnsprechpartner();

        // Nur schon sichtbare, GENANNTE Worte. Keine Belege: die sind sichtbar,
        // aber nicht genannt — sie einer Person zuzuschreiben ist der Schritt,
        // den ADR-0033 ihr selbst vorbehaelt.
        var lage = new Ansprachelage(
            profil.Ueberschrift,
            profil.Genannt,
            request.Gesucht,
            request.Wunsch);

        return await entwerfer.EntwirfAsync(lage, cancellationToken);
    }
}
