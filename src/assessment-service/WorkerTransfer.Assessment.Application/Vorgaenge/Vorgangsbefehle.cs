using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Application.Nachrichten;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Domain.Vorgaenge;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Application.Vorgaenge;

/// <summary>Die Worte, die dieser Dienst in den Postausgang schreibt.</summary>
/// <remarks>
/// <para><strong>Eine Art für beide Bewegungen</strong> — dieselbe Wahl wie bei
/// <c>application_update</c> und <c>transfer_update</c>. Eine Benachrichtigung
/// trägt ohnehin nur eine Art und keinen Inhalt; zwei Arten wären zwei
/// Schalter in den Einstellungen für dieselbe Sache.</para>
///
/// <para><strong>Und sie ist der Grund, warum die Bewertung ankommt.</strong>
/// Ohne diesen Vermerk erführe die Person von einer Rückmeldung erst beim
/// nächsten Vorbeischauen — und eine Beurteilung, die der Beurteilte nicht
/// liest, ist genau das, was hier nicht gebaut wird (ADR-0042 §2).</para>
/// </remarks>
public static class Benachrichtigungsarten
{
    /// <summary>Das Wort auf dem Draht. Dasselbe, das notification-service liest.</summary>
    public const string Bewegung = "assessment_update";
}

// ---------------------------------------------------------------------------
// Stellen
// ---------------------------------------------------------------------------

/// <summary>Ein Unternehmen stellt eine Aufgabe.</summary>
public sealed record AufgabeStellenBefehl(
    SubjectId Wer,
    TenantId Firma,
    string? Titel,
    string? Text,
    int Stunden,
    DateTimeOffset Frist) : IBefehl<Vorgang>;

/// <summary>Legt den Vorgang an — aber nur, wenn die Person freigegeben hat.</summary>
/// <remarks>
/// <para><strong>Die Reihenfolge ist die Zusage.</strong> Zuerst der Ledger:
/// wer diesem Unternehmen nichts freigegeben hat, für den gibt es hier nichts —
/// und zwar dieselbe 404 wie für einen Menschen, den es nicht gibt. Erst danach
/// wird der Rumpf geprüft. Andersherum lernte ein Fremder aus dem Unterschied
/// zwischen 422 und 404, dass seine Angaben in Ordnung waren — und damit, dass
/// es diesen Menschen gibt.</para>
///
/// <para><strong>Keine eigene Fähigkeit, und keine Kopplung an
/// advisor-service.</strong> Eine Aufgabe folgt in der Praxis auf ein Gespräch,
/// aber sie <em>hängt</em> nicht daran: ein Aufruf von hier nach dort machte
/// aus zwei Diensten einen und die Antwort auf „darf ich?" von der
/// Erreichbarkeit eines dritten abhängig. Beide fragen den Ledger, und der
/// Ledger ist die eine Stelle (ADR-0042).</para>
///
/// <para><strong>Die Person erfährt davon</strong> — über den inhaltsfreien
/// Postausgang und in derselben Transaktion wie der Vorgang. Die Nachricht
/// nennt kein Unternehmen und keinen Aufgabentext: sie trägt nur eine Art.</para>
/// </remarks>
public sealed class AufgabeStellenHandler(
    IVorgangsspeicher vorgaenge,
    IEinwilligungstor tor,
    IOutbox postausgang,
    TimeProvider uhr) : IRequestHandler<AufgabeStellenBefehl, Vorgang>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Diese Person hat nichts freigegeben.</exception>
    /// <exception cref="Eingabefehler">Eine Angabe hält die Regel nicht ein.</exception>
    public async Task<Vorgang> Handle(
        AufgabeStellenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await tor.DarfSehenAsync(request.Wer, request.Firma, cancellationToken))
        {
            throw new KeinVorgang();
        }

        var jetzt = uhr.GetUtcNow();

        var aufgabe = Aufgabe.Schreibe(
            request.Titel, request.Text, request.Stunden, request.Frist, jetzt);

        var vorgang = Vorgang.Stelle(request.Wer, request.Firma, aufgabe, jetzt);

        await vorgaenge.SichereAsync(vorgang, cancellationToken);
        await postausgang.VermerkeAsync(
            request.Wer, Benachrichtigungsarten.Bewegung, cancellationToken);

        return vorgang;
    }
}

// ---------------------------------------------------------------------------
// Einreichen
// ---------------------------------------------------------------------------

/// <summary>Die Person reicht ihre Lösung ein.</summary>
public sealed record EinreichenBefehl(
    Guid Id, SubjectId Wer, string? Text, string? Adresse) : IBefehl<Vorgang>;

/// <summary>Nimmt die Lösung an — und fragt den Ledger dabei nicht.</summary>
/// <remarks>
/// <para><strong>Die Personenseite ist nie vom Ledger bewacht</strong>
/// (ADR-0042 §2). Wer auf eine Aufgabe antworten will, die ihm gestellt wurde,
/// braucht dafür keine Freigabe — die Freigabe regelt, was das Unternehmen
/// <em>sieht</em>, nicht, was die Person <em>tun</em> darf. Der umgekehrte Bau
/// verlangte von einem Menschen, sichtbar zu bleiben, um seine eigene Arbeit
/// abgeben zu können.</para>
///
/// <para>Ein fremder Vorgang antwortet mit derselben 404 wie ein Vorgang, den
/// es nicht gibt: eine 403 sagte „den gibt es, er gehört dir nur nicht".</para>
///
/// <para><strong>Kein Vermerk im Postausgang, und das ist kein Vergessen.</strong>
/// Benachrichtigt werden Menschen; ein Unternehmen hat kein Postfach. Es sieht
/// die Einreichung in seiner eigenen Liste, hinter der Anmeldung — dort, wo
/// geprüft wird, wer liest. Die zwei Vermerke, die dieser Dienst schreibt,
/// gehen beide an die Person: „dir wurde eine Aufgabe gestellt" und „deine
/// Bewertung liegt vor".</para>
/// </remarks>
public sealed class EinreichenHandler(IVorgangsspeicher vorgaenge, TimeProvider uhr)
    : IRequestHandler<EinreichenBefehl, Vorgang>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Gibt es nicht, oder gehört jemand anderem.</exception>
    /// <exception cref="SchrittNichtMoeglich">Schon eingereicht, oder Frist vorbei.</exception>
    public async Task<Vorgang> Handle(
        EinreichenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vorgang = await vorgaenge.HoleAsync(request.Id, cancellationToken);

        if (vorgang is null || vorgang.Wer != request.Wer)
        {
            throw new KeinVorgang();
        }

        var jetzt = uhr.GetUtcNow();

        vorgang.Reiche_ein(
            Einreichung.Schreibe(request.Text, request.Adresse, jetzt), jetzt);

        await vorgaenge.SichereAsync(vorgang, cancellationToken);

        return vorgang;
    }
}

// ---------------------------------------------------------------------------
// Bewerten
// ---------------------------------------------------------------------------

/// <summary>Das Unternehmen schreibt seine Rückmeldung.</summary>
public sealed record BewertenBefehl(
    Guid Id, TenantId Firma, Ausgang Ausgang, string? Text) : IBefehl<Vorgang>;

/// <summary>
/// Schreibt die eine Bewertung — und vermerkt im selben Atemzug, dass die
/// Person sie lesen soll.
/// </summary>
/// <remarks>
/// <para><strong>Das ist die Zeile, an der ADR-0042 §2 hängt.</strong> Die
/// Bewertung und der Vermerk entstehen in derselben Transaktion: eine
/// Bewertung ohne den Vermerk wäre eine Beurteilung, von der die Person nichts
/// erfährt, und der Vermerk ohne die Bewertung eine Nachricht über nichts.</para>
///
/// <para><strong>Der Text ist Pflicht — auch bei einer Absage.</strong> Das
/// steht im Aggregat und nicht hier, damit es kein Handler umgehen kann.</para>
/// </remarks>
public sealed class BewertenHandler(
    IVorgangsspeicher vorgaenge, IEinwilligungstor tor, IOutbox postausgang, TimeProvider uhr)
    : IRequestHandler<BewertenBefehl, Vorgang>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Gibt es nicht, gehört anderen, oder nichts freigegeben.</exception>
    /// <exception cref="SchrittNichtMoeglich">Nichts eingereicht, oder schon bewertet.</exception>
    /// <exception cref="Eingabefehler">Der Text fehlt.</exception>
    public async Task<Vorgang> Handle(
        BewertenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vorgang = await vorgaenge.HoleAsync(request.Id, cancellationToken);

        if (vorgang is null || vorgang.Firma != request.Firma)
        {
            throw new KeinVorgang();
        }

        if (!await tor.DarfSehenAsync(vorgang.Wer, request.Firma, cancellationToken))
        {
            throw new KeinVorgang();
        }

        var jetzt = uhr.GetUtcNow();

        vorgang.Bewerte(Bewertung.Schreibe(request.Text, request.Ausgang, jetzt), jetzt);

        await vorgaenge.SichereAsync(vorgang, cancellationToken);
        await postausgang.VermerkeAsync(
            vorgang.Wer, Benachrichtigungsarten.Bewegung, cancellationToken);

        return vorgang;
    }
}

// ---------------------------------------------------------------------------
// Lesen
// ---------------------------------------------------------------------------

/// <summary>„Zeig mir diesen Vorgang."</summary>
/// <remarks>
/// Eine Abfrage für <em>beide</em> Seiten, und daran hängt die Zusage: es gibt
/// keine Firmensicht neben einer Personensicht, weil es nur eine Abfrage gibt.
/// </remarks>
/// <param name="Id">Welcher Vorgang.</param>
/// <param name="Wer">Wer fragt — aus dem geprüften Token.</param>
/// <param name="Firma">In wessen Namen, oder <c>null</c> für „als Person".</param>
public sealed record VorgangAbfrage(Guid Id, SubjectId Wer, TenantId? Firma)
    : IAbfrage<Vorgang>;

/// <summary>Beantwortet sie für die Person ohne Ledger und für die Firma mit.</summary>
/// <remarks>
/// <para><strong>Die Personenseite gewinnt.</strong> Wer seinen eigenen Vorgang
/// aufruft, bekommt ihn — auch während er gerade für ein Unternehmen handelt,
/// und auch, nachdem er jede Sichtbarkeit widerrufen hat. Genau das ist „die
/// Person sieht die Bewertung, immer" (ADR-0042 §2).</para>
///
/// <para><strong>Für die Firma steht der Ledger davor</strong>, bei jedem
/// Lesen (ADR-0013). Wer widerruft, verschwindet für das Unternehmen — 404, wie
/// überall.</para>
/// </remarks>
public sealed class VorgangHandler(IVorgangsspeicher vorgaenge, IEinwilligungstor tor)
    : IRequestHandler<VorgangAbfrage, Vorgang>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Vier Lagen, eine Antwort.</exception>
    public async Task<Vorgang> Handle(VorgangAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vorgang = await vorgaenge.HoleAsync(request.Id, cancellationToken);

        if (vorgang is null)
        {
            throw new KeinVorgang();
        }

        if (vorgang.Wer == request.Wer)
        {
            return vorgang;
        }

        if (request.Firma is not { } firma || vorgang.Firma != firma)
        {
            throw new KeinVorgang();
        }

        return await tor.DarfSehenAsync(vorgang.Wer, firma, cancellationToken)
            ? vorgang
            : throw new KeinVorgang();
    }
}

/// <summary>„Zeig mir meine Vorgänge."</summary>
public sealed record MeineVorgaengeAbfrage(SubjectId Wer) : IAbfrage<IReadOnlyList<Vorgang>>;

/// <summary>Die eigene Liste — ungefiltert, und ohne den Ledger zu fragen.</summary>
/// <remarks>
/// Die eigene Einwilligung zu prüfen, um die eigene Arbeit zu sehen, wäre nicht
/// nur ein überflüssiger Sprung: wer widerrufen hat, käme sonst an seine
/// Bewertungen nicht mehr heran (ADR-0020 §5, ADR-0042 §2).
/// </remarks>
public sealed class MeineVorgaengeHandler(IVorgangsspeicher vorgaenge)
    : IRequestHandler<MeineVorgaengeAbfrage, IReadOnlyList<Vorgang>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Vorgang>> Handle(
        MeineVorgaengeAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return vorgaenge.FuerPersonAsync(request.Wer, cancellationToken);
    }
}

/// <summary>„Zeig uns unsere Vorgänge."</summary>
public sealed record FirmenvorgaengeAbfrage(TenantId Firma) : IAbfrage<IReadOnlyList<Vorgang>>;

/// <summary>Die Liste des Unternehmens — je Zeile über den Ledger gefiltert.</summary>
/// <remarks>
/// <para><strong>Eine Sammelfrage, kein Zwischenspeicher</strong> (ADR-0030,
/// ADR-0013). Wer widerrufen hat, fällt aus der Liste — beim nächsten Aufruf,
/// nicht beim übernächsten.</para>
///
/// <para><strong>Und keine Gesamtzahl</strong> (ADR-0026): sie verriete über
/// die Differenz zur Länge, wie viele Menschen dem Unternehmen die Sicht
/// entzogen haben. Die kurze Liste wird auch nicht aufgefüllt.</para>
/// </remarks>
public sealed class FirmenvorgaengeHandler(IVorgangsspeicher vorgaenge, IEinwilligungstor tor)
    : IRequestHandler<FirmenvorgaengeAbfrage, IReadOnlyList<Vorgang>>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<IReadOnlyList<Vorgang>> Handle(
        FirmenvorgaengeAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alle = await vorgaenge.FuerFirmaAsync(request.Firma, cancellationToken);

        if (alle.Count == 0)
        {
            return alle;
        }

        var duerfen = await tor.DuerfenSehenAsync(
            [.. alle.Select(vorgang => (vorgang.Wer, request.Firma))], cancellationToken);

        if (duerfen.Count != alle.Count)
        {
            // Eine unpassende Antwort wird abgewiesen statt geraten: sie falsch
            // zuzuordnen hiesse, die Freigabe des falschen Menschen zu lesen.
            throw new EinwilligungSchweigt(
                $"{duerfen.Count} Antworten auf {alle.Count} Fragen");
        }

        return [.. alle.Where((_, i) => duerfen[i])];
    }
}
