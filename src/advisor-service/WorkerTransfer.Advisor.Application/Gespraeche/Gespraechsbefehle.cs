using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Nachrichten;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandate;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Advisor.Application.Gespraeche;

/// <summary>Es gibt hier nichts zu sehen.</summary>
/// <remarks>
/// <para><strong>Ein Ausgang für drei Lagen, und das ist der Kern dieses
/// Dienstes:</strong> das Gespräch existiert nicht; es existiert, steht aber auf
/// keiner Stufe mehr; die Person hat dieses Unternehmen ausgeschlossen. Alle
/// drei antworten 404, bis auf die Korrelations-ID byte-identisch.</para>
///
/// <para>Ein eigener Code oder eine eigene Meldung für „existiert, zeigt sich
/// aber nicht" wäre ein Orakel — und ein „gesperrt"-Hinweis verriete genau das,
/// was in dieser Stufe nicht freigegeben ist: dass es etwas gibt.</para>
/// </remarks>
public sealed class KeinGespraech() : Exception("No such conversation");

// ---------------------------------------------------------------------------
// Eröffnen
// ---------------------------------------------------------------------------

/// <summary>Ein Unternehmen eröffnet ein Gespräch.</summary>
public sealed record GespraechEroeffnenBefehl(
    SubjectId Wer, TenantId Firma, string? Anlass) : IBefehl<(Gespraechsansicht Ansicht, bool Neu)>;

/// <summary>
/// Legt es an — aber nur, wenn Stufe 1 schon steht und niemand ausgeschlossen
/// wurde.
/// </summary>
/// <remarks>
/// <para><strong>Die Reihenfolge ist die Zusage.</strong> Zuerst der Ledger:
/// wer diesem Unternehmen nichts freigegeben hat, für den gibt es hier nichts —
/// und zwar dieselbe 404 wie für einen Menschen, den es nicht gibt. Erst danach
/// das Mandat, und auch ein Ausschluss antwortet mit derselben 404. Ein
/// eigener Code sagte „du stehst auf ihrer Liste", und das ist eine Auskunft
/// über einen Menschen an das Unternehmen, vor dem sie sich schützt.</para>
///
/// <para><strong>Ein Gespräch je Paar.</strong> Läuft schon eines, kommt es
/// zurück, statt dass ein zweites entsteht: zwei Gespräche zwischen denselben
/// beiden meinten dieselben Fähigkeiten und stünden auf zwei Ständen, die
/// auseinanderlaufen.</para>
///
/// <para><strong>Die Person erfährt davon</strong> — über den inhaltsfreien
/// Postausgang (ADR-0025) und in derselben Transaktion wie das Gespräch. Die
/// Nachricht nennt kein Unternehmen: sie trägt nur eine Art, und wer es war,
/// steht hinter der Anmeldung in ihrer eigenen Liste.</para>
/// </remarks>
public sealed class GespraechEroeffnenHandler(
    IGespraechsspeicher gespraeche,
    IMandatspeicher mandate,
    IEinwilligungstor tor,
    IFirmenauskunft firmen,
    IOutbox postausgang,
    TimeProvider uhr)
    : IRequestHandler<GespraechEroeffnenBefehl, (Gespraechsansicht Ansicht, bool Neu)>
{
    /// <summary>Das Wort auf dem Draht. Dasselbe, das notification-service liest.</summary>
    public const string Art = "advisor_conversation";

    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Nichts freigegeben, oder ausgeschlossen.</exception>
    public async Task<(Gespraechsansicht, bool)> Handle(
        GespraechEroeffnenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stufen = await tor.StufenAsync([(request.Wer, request.Firma)], cancellationToken);
        var stufe = stufen[0];

        if (stufe < Stufe.Profil)
        {
            throw new KeinGespraech();
        }

        var mandat = await mandate.HoleAsync(request.Wer, cancellationToken);

        if (mandat is not null && await Ausgeschlossen(mandat, request.Firma, cancellationToken))
        {
            throw new KeinGespraech();
        }

        if (await gespraeche.HoleLaufendesAsync(request.Wer, request.Firma, cancellationToken)
            is { } laufendes)
        {
            return (Gespraechsansicht.Baue(laufendes, stufe, mandat, null), false);
        }

        var gespraech = Gespraech.Eroeffne(
            request.Wer, request.Firma, request.Anlass, uhr.GetUtcNow());

        await gespraeche.SichereAsync(gespraech, cancellationToken);
        await postausgang.VermerkeAsync(request.Wer, Art, cancellationToken);

        return (Gespraechsansicht.Baue(gespraech, stufe, mandat, null), true);
    }

    private async Task<bool> Ausgeschlossen(
        Mandat mandat, TenantId firma, CancellationToken cancellationToken)
    {
        if (mandat.AusgeschlosseneUnternehmen.Count == 0)
        {
            // Kein Ausschluss heisst: gar nicht erst fragen. Ein Aufruf an
            // identity-service fuer jede Eroeffnung waere ein Sprung, der in
            // fast allen Faellen nichts entscheidet.
            return false;
        }

        var bild = await firmen.HoleAsync(firma, cancellationToken);

        return mandat.Schliesst_aus(bild?.Domain);
    }
}

// ---------------------------------------------------------------------------
// Lesen
// ---------------------------------------------------------------------------

/// <summary>„Zeig mir meine Gespräche."</summary>
public sealed record MeineGespraecheAbfrage(SubjectId Wer)
    : IAbfrage<IReadOnlyList<Gespraechsansicht>>;

/// <summary>Die Person sieht alle ihre Gespräche, auch die zurückgezogenen.</summary>
/// <remarks>
/// <strong>Ohne Stufenfilter</strong>, anders als die Firmenliste: wer eine
/// Stufe zurückgenommen hat, muss sie wiedergeben können. Eine Liste, aus der
/// ein Gespräch nach dem Widerruf verschwände, machte den Widerruf zur
/// Einbahnstraße.
/// <para>
/// Das Mandat kommt nicht mit: es ist ihr eigenes und steht unter
/// <c>/advisor/me/mandate</c>. Zweimal dasselbe auszuliefern wären zwei Stellen,
/// an denen es später auseinanderläuft.
/// </para>
/// </remarks>
public sealed class MeineGespraecheHandler(
    IGespraechsspeicher gespraeche, IEinwilligungstor tor)
    : IRequestHandler<MeineGespraecheAbfrage, IReadOnlyList<Gespraechsansicht>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Gespraechsansicht>> Handle(
        MeineGespraecheAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var meine = await gespraeche.FuerPersonAsync(request.Wer, cancellationToken);

        if (meine.Count == 0)
        {
            return [];
        }

        var stufen = await tor.StufenAsync(
            [.. meine.Select(g => (g.Wer, g.Firma))], cancellationToken);

        return [.. meine.Select((g, i) => new Gespraechsansicht(g, stufen[i]))];
    }
}

/// <summary>„Zeig uns unsere Gespräche."</summary>
public sealed record FirmengespraecheAbfrage(TenantId Firma)
    : IAbfrage<IReadOnlyList<Gespraechsansicht>>;

/// <summary>
/// Das Unternehmen sieht, was auf Stufe 1 oder höher steht — und sonst nichts.
/// </summary>
/// <remarks>
/// <para><strong>Ein Gespräch auf Stufe 0 fällt aus der Liste</strong>, statt
/// leer darin zu stehen. Eine Zeile ohne Inhalt wäre der „gesperrt"-Hinweis in
/// Listenform: sie sagte, dass es diesen Menschen gibt und dass er
/// zurückgezogen hat. Beides geht das Unternehmen nichts an.</para>
///
/// <para>Und deshalb gibt es <strong>keine Gesamtzahl</strong> (ADR-0026): über
/// die Differenz zur Länge verriete sie genau die Zeilen, die eben
/// herausgefallen sind.</para>
///
/// <para>Der Klarname wird <strong>erst nach</strong> der Stufenauskunft geholt,
/// und nur für die Zeilen, die Stufe 3 tragen. Über wen nichts freigegeben ist,
/// über den wird auch nichts nachgeschlagen.</para>
/// </remarks>
public sealed class FirmengespraecheHandler(
    IGespraechsspeicher gespraeche,
    IMandatspeicher mandate,
    IEinwilligungstor tor,
    IPersonenauskunft personen)
    : IRequestHandler<FirmengespraecheAbfrage, IReadOnlyList<Gespraechsansicht>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Gespraechsansicht>> Handle(
        FirmengespraecheAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ihre = await gespraeche.FuerFirmaAsync(request.Firma, cancellationToken);

        if (ihre.Count == 0)
        {
            return [];
        }

        var stufen = await tor.StufenAsync(
            [.. ihre.Select(g => (g.Wer, g.Firma))], cancellationToken);

        var ansichten = new List<Gespraechsansicht>(ihre.Count);

        for (var i = 0; i < ihre.Count; i++)
        {
            if (stufen[i] < Stufe.Profil)
            {
                continue;
            }

            var mandat = await mandate.HoleAsync(ihre[i].Wer, cancellationToken);

            var person = stufen[i] >= Stufe.Person
                ? await personen.HoleAsync(ihre[i].Wer, cancellationToken)
                : null;

            ansichten.Add(Gespraechsansicht.Baue(ihre[i], stufen[i], mandat, person));
        }

        return ansichten;
    }
}

/// <summary>„Zeig uns dieses eine Gespräch."</summary>
public sealed record FirmengespraechAbfrage(Guid Id, TenantId Firma)
    : IAbfrage<Gespraechsansicht>;

/// <summary>Eines — oder 404, aus jedem der drei Gründe.</summary>
public sealed class FirmengespraechHandler(
    IGespraechsspeicher gespraeche,
    IMandatspeicher mandate,
    IEinwilligungstor tor,
    IPersonenauskunft personen)
    : IRequestHandler<FirmengespraechAbfrage, Gespraechsansicht>
{
    /// <inheritdoc />
    /// <exception cref="KeinGespraech">Gibt es nicht, gehört anderen, oder Stufe 0.</exception>
    public async Task<Gespraechsansicht> Handle(
        FirmengespraechAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gespraech = await gespraeche.HoleAsync(request.Id, cancellationToken);

        // „Gehoert einem anderen Unternehmen" und „gibt es nicht" antworten
        // gleich: ein Unterschied verriete, dass dieses Gespraech existiert.
        if (gespraech is null || gespraech.Firma != request.Firma)
        {
            throw new KeinGespraech();
        }

        var stufen = await tor.StufenAsync(
            [(gespraech.Wer, gespraech.Firma)], cancellationToken);

        if (stufen[0] < Stufe.Profil)
        {
            throw new KeinGespraech();
        }

        var mandat = await mandate.HoleAsync(gespraech.Wer, cancellationToken);

        var person = stufen[0] >= Stufe.Person
            ? await personen.HoleAsync(gespraech.Wer, cancellationToken)
            : null;

        return Gespraechsansicht.Baue(gespraech, stufen[0], mandat, person);
    }
}
