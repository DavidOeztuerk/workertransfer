using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Nachrichten;

namespace WorkerTransfer.Scout.Application.Kandidaten;

/// <summary>„Sag diesen Menschen, dass ihr Profil entdeckt wurde."</summary>
/// <param name="Wer">Die Menschen einer Trefferseite.</param>
public sealed record EntdeckungMeldenBefehl(IReadOnlyList<SubjectId> Wer) : IBefehl<int>;

/// <summary>Vermerkt je Mensch eine Absicht im Postausgang. Mehr nicht.</summary>
/// <remarks>
/// <para><strong>Die fünfte Entscheidung aus ADR-0036 und die Auskunft aus
/// ADR-0033.</strong> Der Entwurf wollte einmal <c>GET /me/scouting</c> — eine
/// Tabelle „wer hat wen angesehen". Die entsteht hier nicht und soll nicht
/// entstehen: sie wäre die sensibelste Tabelle des Systems. Stattdessen eine
/// <em>Nachricht</em>, wie bei einem sozialen Netz.</para>
///
/// <para><strong>Der Postausgang bleibt inhaltsfrei</strong> (ADR-0025): eine
/// Kennung und eine Art, nie ein Inhalt. Die Nachricht nennt deshalb
/// <strong>kein Unternehmen</strong> — „ein Unternehmen" genügt. Wer sucht, ist
/// eine Aussage über das Unternehmen, und die Person kann damit nichts anfangen,
/// solange niemand sie angesprochen hat. Der Mandant taucht in diesem Befehl
/// folgerichtig gar nicht auf.</para>
///
/// <para><strong>Höchstens eine je Person und Tag</strong> — und diese Kappe
/// liegt bei notification-service, nicht hier. Das ist Absicht: die Zustellung
/// ist mindestens-einmal, eine zweite Zeile also jederzeit möglich, und eine
/// Kappe im Absender wäre eine Tabelle „diese Person wurde am … gemeldet" —
/// wieder ein Verzeichnis über die Sichtbarkeit eines Menschen. Der Empfänger
/// kann dieselbe Zusage aus seinem eigenen Postfach beantworten, ohne dass
/// irgendwo eine neue Zeile entsteht.</para>
///
/// <para>Entdoppelt wird trotzdem, innerhalb dieses einen Aufrufs: derselbe
/// Mensch soll für eine Seite nicht zweimal vermerkt werden.</para>
/// </remarks>
public sealed class EntdeckungMeldenHandler(IOutbox postausgang)
    : IRequestHandler<EntdeckungMeldenBefehl, int>
{
    /// <summary>Das Wort auf dem Draht. Dasselbe, das notification-service liest.</summary>
    /// <remarks>
    /// Zwei Schreibweisen für dieselbe Art wären zwei Gelegenheiten, eine
    /// Nachricht stillschweigend fallen zu lassen.
    /// </remarks>
    public const string Art = "profile_discovered";

    /// <inheritdoc />
    /// <returns>Wie viele Absichten vermerkt wurden.</returns>
    public async Task<int> Handle(
        EntdeckungMeldenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vermerkt = 0;

        foreach (var wer in request.Wer.Distinct())
        {
            await postausgang.VermerkeAsync(wer, Art, cancellationToken);
            vermerkt++;
        }

        return vermerkt;
    }
}
