using Noelia.Core.Identity;
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
/// <para><strong>Und sie ist der Grund, warum die Evaluation ankommt.</strong>
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
// Jobs
// ---------------------------------------------------------------------------

/// <summary>Ein Unternehmen stellt eine Aufgabe.</summary>
public sealed record SetTaskCommand(
    SubjectId Wer,
    TenantId Firma,
    string? Titel,
    string? Text,
    int Stunden,
    DateTimeOffset Frist) : IBefehl<Case>;

/// <summary>Legt den Case an — aber nur, wenn die Person freigegeben hat.</summary>
/// <remarks>
/// <para><strong>Die Reihenfolge ist die Zusage.</strong> Zuerst der Ledger:
/// wer diesem Unternehmen nichts freigegeben hat, für den gibt es hier nichts —
/// und zwar dieselbe 404 wie für einen Menschen, den es nicht gibt. Erst danach
/// wird der Rumpf geprüft. Andersherum lernte ein Fremder aus dem Unterschied
/// zwischen 422 und 404, dass seine Angaben in Ordnung waren — und damit, dass
/// es diesen Menschen gibt.</para>
///
/// <para><strong>None eigene Fähigkeit, und keine Kopplung an
/// advisor-service.</strong> Eine Aufgabe folgt in der Praxis auf ein Gespräch,
/// aber sie <em>hängt</em> nicht daran: ein Aufruf von hier nach dort machte
/// aus zwei Diensten einen und die Antwort auf „darf ich?" von der
/// Availability eines dritten abhängig. Beide fragen den Ledger, und der
/// Ledger ist die eine Job (ADR-0042).</para>
///
/// <para><strong>Die Person erfährt davon</strong> — über den inhaltsfreien
/// Postausgang und in derselben Transaktion wie der Case. Die Nachricht
/// nennt kein Unternehmen und keinen Aufgabentext: sie trägt nur eine Art.</para>
/// </remarks>
public sealed class SetTaskHandler(
    ICaseStore cases,
    IConsentGate tor,
    IOutbox postausgang,
    TimeProvider uhr) : IRequestHandler<SetTaskCommand, Case>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Diese Person hat nichts freigegeben.</exception>
    /// <exception cref="Eingabefehler">Eine Angabe hält die Regel nicht ein.</exception>
    public async Task<Case> Handle(
        SetTaskCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await tor.MaySeeAsync(request.Wer, request.Firma, cancellationToken))
        {
            throw new NoCase();
        }

        var now = uhr.GetUtcNow();

        var aufgabe = Aufgabe.Write(
            request.Titel, request.Text, request.Stunden, request.Frist, now);

        var caseItem = Case.Job(request.Wer, request.Firma, aufgabe, now);

        await cases.SaveAsync(caseItem, cancellationToken);
        await postausgang.NoteAsync(
            request.Wer, Benachrichtigungsarten.Bewegung, cancellationToken);

        return caseItem;
    }
}

// ---------------------------------------------------------------------------
// Einreichen
// ---------------------------------------------------------------------------

/// <summary>Die Person reicht ihre Lösung ein.</summary>
public sealed record SubmitCommand(
    Guid Id, SubjectId Wer, string? Text, string? Adresse) : IBefehl<Case>;

/// <summary>Nimmt die Lösung an — und fragt den Ledger dabei nicht.</summary>
/// <remarks>
/// <para><strong>Die Personenseite ist nie vom Ledger bewacht</strong>
/// (ADR-0042 §2). Wer auf eine Aufgabe antworten will, die ihm gestellt wurde,
/// braucht dafür keine Release — die Release regelt, was das Unternehmen
/// <em>sieht</em>, nicht, was die Person <em>tun</em> darf. Der umgekehrte Bau
/// verlangte von einem Menschen, sichtbar zu bleiben, um seine eigene Arbeit
/// abgeben zu können.</para>
///
/// <para>Ein fremder Case antwortet mit derselben 404 wie ein Case, den
/// es nicht gibt: eine 403 sagte „den gibt es, er gehört dir nur nicht".</para>
///
/// <para><strong>Kein Vermerk im Postausgang, und das ist kein Vergessen.</strong>
/// Benachrichtigt werden Menschen; ein Unternehmen hat kein Inbox. Es sieht
/// die Submission in seiner eigenen Liste, hinter der Anmeldung — dort, wo
/// geprüft wird, wer liest. Die zwei Vermerke, die dieser Dienst schreibt,
/// gehen beide an die Person: „dir wurde eine Aufgabe gestellt" und „deine
/// Evaluation liegt vor".</para>
/// </remarks>
public sealed class SubmitHandler(ICaseStore cases, TimeProvider uhr)
    : IRequestHandler<SubmitCommand, Case>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Gibt es nicht, oder gehört jemand anderem.</exception>
    /// <exception cref="SchrittNichtMoeglich">Schon eingereicht, oder Frist vorbei.</exception>
    public async Task<Case> Handle(
        SubmitCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Wer != request.Wer)
        {
            throw new NoCase();
        }

        var now = uhr.GetUtcNow();

        caseItem.Reiche_ein(
            Submission.Write(request.Text, request.Adresse, now), now);

        await cases.SaveAsync(caseItem, cancellationToken);

        return caseItem;
    }
}

// ---------------------------------------------------------------------------
// Bewerten
// ---------------------------------------------------------------------------

/// <summary>Das Unternehmen schreibt seine Rückmeldung.</summary>
public sealed record EvaluateCommand(
    Guid Id, TenantId Firma, Ausgang Ausgang, string? Text) : IBefehl<Case>;

/// <summary>
/// Schreibt die eine Evaluation — und vermerkt im selben Atemzug, dass die
/// Person sie lesen soll.
/// </summary>
/// <remarks>
/// <para><strong>Das ist die Zeile, an der ADR-0042 §2 hängt.</strong> Die
/// Evaluation und der Vermerk entstehen in derselben Transaktion: eine
/// Evaluation ohne den Vermerk wäre eine Beurteilung, von der die Person nichts
/// erfährt, und der Vermerk ohne die Evaluation eine Nachricht über nichts.</para>
///
/// <para><strong>Der Text ist Pflicht — auch bei einer Absage.</strong> Das
/// steht im Aggregat und nicht hier, damit es kein Handler umgehen kann.</para>
/// </remarks>
public sealed class EvaluateHandler(
    ICaseStore cases, IConsentGate tor, IOutbox postausgang, TimeProvider uhr)
    : IRequestHandler<EvaluateCommand, Case>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Gibt es nicht, gehört anderen, oder nichts freigegeben.</exception>
    /// <exception cref="SchrittNichtMoeglich">Nichts eingereicht, oder schon bewertet.</exception>
    /// <exception cref="Eingabefehler">Der Text fehlt.</exception>
    public async Task<Case> Handle(
        EvaluateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Firma != request.Firma)
        {
            throw new NoCase();
        }

        if (!await tor.MaySeeAsync(caseItem.Wer, request.Firma, cancellationToken))
        {
            throw new NoCase();
        }

        var now = uhr.GetUtcNow();

        caseItem.Bewerte(Evaluation.Write(request.Text, request.Ausgang, now), now);

        await cases.SaveAsync(caseItem, cancellationToken);
        await postausgang.NoteAsync(
            caseItem.Wer, Benachrichtigungsarten.Bewegung, cancellationToken);

        return caseItem;
    }
}

// ---------------------------------------------------------------------------
// Lesen
// ---------------------------------------------------------------------------

/// <summary>„Zeig mir diesen Case."</summary>
/// <remarks>
/// Eine Abfrage für <em>beide</em> Seiten, und daran hängt die Zusage: es gibt
/// keine Firmensicht neben einer Personensicht, weil es nur eine Abfrage gibt.
/// </remarks>
/// <param name="Id">Welcher Case.</param>
/// <param name="Wer">Wer fragt — aus dem geprüften Token.</param>
/// <param name="Firma">In wessen Namen, oder <c>null</c> für „als Person".</param>
public sealed record CaseQuery(Guid Id, SubjectId Wer, TenantId? Firma)
    : IAbfrage<Case>;

/// <summary>Beantwortet sie für die Person ohne Ledger und für die Firma mit.</summary>
/// <remarks>
/// <para><strong>Die Personenseite gewinnt.</strong> Wer seinen eigenen Case
/// aufruft, bekommt ihn — auch während er gerade für ein Unternehmen handelt,
/// und auch, nachdem er jede Sichtbarkeit widerrufen hat. Genau das ist „die
/// Person sieht die Evaluation, immer" (ADR-0042 §2).</para>
///
/// <para><strong>Für die Firma steht der Ledger davor</strong>, bei jedem
/// Lesen (ADR-0013). Wer widerruft, verschwindet für das Unternehmen — 404, wie
/// überall.</para>
/// </remarks>
public sealed class CaseHandler(ICaseStore cases, IConsentGate tor)
    : IRequestHandler<CaseQuery, Case>
{
    /// <inheritdoc />
    /// <exception cref="KeinVorgang">Vier Lagen, eine Antwort.</exception>
    public async Task<Case> Handle(CaseQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.Id, cancellationToken);

        if (caseItem is null)
        {
            throw new NoCase();
        }

        if (caseItem.Wer == request.Wer)
        {
            return caseItem;
        }

        if (request.Firma is not { } company || caseItem.Firma != company)
        {
            throw new NoCase();
        }

        return await tor.MaySeeAsync(caseItem.Wer, company, cancellationToken)
            ? caseItem
            : throw new NoCase();
    }
}

/// <summary>„Zeig mir meine Vorgänge."</summary>
public sealed record MyCasesQuery(SubjectId Wer) : IAbfrage<IReadOnlyList<Case>>;

/// <summary>Die eigene Liste — ungefiltert, und ohne den Ledger zu fragen.</summary>
/// <remarks>
/// Die eigene Einwilligung zu prüfen, um die eigene Arbeit zu sehen, wäre nicht
/// nur ein überflüssiger Sprung: wer widerrufen hat, käme sonst an seine
/// Bewertungen nicht mehr heran (ADR-0020 §5, ADR-0042 §2).
/// </remarks>
public sealed class MyCasesHandler(ICaseStore cases)
    : IRequestHandler<MyCasesQuery, IReadOnlyList<Case>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Case>> Handle(
        MyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return cases.ForPersonAsync(request.Wer, cancellationToken);
    }
}

/// <summary>„Zeig uns unsere Vorgänge."</summary>
public sealed record CompanyCasesQuery(TenantId Firma) : IAbfrage<IReadOnlyList<Case>>;

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
public sealed class CompanyCasesHandler(ICaseStore cases, IConsentGate tor)
    : IRequestHandler<CompanyCasesQuery, IReadOnlyList<Case>>
{
    /// <inheritdoc />
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    public async Task<IReadOnlyList<Case>> Handle(
        CompanyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var alle = await cases.ForCompanyAsync(request.Firma, cancellationToken);

        if (alle.Count == 0)
        {
            return alle;
        }

        var duerfen = await tor.MayEachSeeAsync(
            [.. alle.Select(caseItem => (caseItem.Wer, request.Firma))], cancellationToken);

        if (duerfen.Count != alle.Count)
        {
            // Eine unpassende Antwort wird abgewiesen statt geraten: sie falsch
            // zuzuordnen hiesse, die Release des falschen Menschen zu lesen.
            throw new ConsentGateSilent(
                $"{duerfen.Count} Antworten auf {alle.Count} Fragen");
        }

        return [.. alle.Where((_, i) => duerfen[i])];
    }
}
