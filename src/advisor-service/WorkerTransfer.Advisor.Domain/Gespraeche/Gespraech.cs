using Noelia.Core.Identity;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Domain.Gespraeche;

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(ConversationState now, string was)
    : Exception($"A {ConversationStates.Wort(now)} conversation cannot be {was}");

/// <summary>Das ist das Gespräch eines anderen Menschen.</summary>
public sealed class NichtDeins() : Exception("This conversation belongs to someone else");

/// <summary>Wo ein Gespräch steht.</summary>
/// <remarks>
/// Vier Stände, und keiner davon ist eine Stufe. Der State sagt, ob noch
/// gesprochen wird; die <see cref="Stufe"/> sagt, wie weit geöffnet ist — und
/// sie kommt aus dem Ledger, nicht von hier.
/// </remarks>
public enum ConversationState
{
    /// <summary>Man spricht.</summary>
    Laufend,

    /// <summary>Die Person ist einverstanden. Jetzt darf übergeben werden.</summary>
    Zugestimmt,

    /// <summary>Übergeben an transfer-service. Hier ist es damit zu Ende.</summary>
    Uebergeben,

    /// <summary>Beendet — von wem auch immer.</summary>
    Beendet
}

/// <summary>Die Worte, mit denen ein State auf der Leitung steht.</summary>
public static class ConversationStates
{
    /// <summary>Das Wort zum State.</summary>
    public static string Wort(ConversationState state) => state switch
    {
        ConversationState.Laufend => "talking",
        ConversationState.Zugestimmt => "agreed",
        ConversationState.Uebergeben => "handed_over",
        ConversationState.Beendet => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>Die Stände, in denen noch gesprochen wird.</summary>
    public static readonly IReadOnlyList<ConversationState> Laufende =
    [
        ConversationState.Laufend, ConversationState.Zugestimmt
    ];
}

/// <summary>
/// Ein Gespräch zwischen einem Menschen und einem Unternehmen.
/// </summary>
/// <remarks>
/// <para><strong>Es lebt hier, solange es um Stufen und Mandate geht</strong>
/// (ADR-0037 Entscheidung 4). Erst die Einigung wird ein Case in
/// transfer-service — und dort steht der Dreieckskonsens, der hier
/// ausdrücklich <em>nicht</em> nachgebaut wird.</para>
///
/// <para><strong>None Stufenspalte.</strong> Ein <c>hasStage</c> wäre die
/// Stufe als zweite Tür neben dem Ledger; die Stufe wird bei jedem Lesen dort
/// erfragt. Was dieses Aggregat hält, ist die Beziehung und ihr State, sonst
/// nichts.</para>
///
/// <para><strong>Und kein Nachrichtenverlauf.</strong> ADR-0037 Entscheidung 5
/// lässt offen, ob Menschen hier tippen; solange das offen ist, gibt es keine
/// Tabelle dafür — und damit auch keine Frage, ob eine KI hineinschreiben darf.
/// Der <see cref="Anlass"/> ist das eine Feld, das die Firma beim Eröffnen
/// schreibt.</para>
/// </remarks>
public sealed class Conversation
{
    /// <summary>Wie lang der Anlass sein darf.</summary>
    public const int HoechstlaengeAnlass = 2000;

    private Conversation(
        Guid id,
        SubjectId subject,
        TenantId company,
        ConversationState state,
        string anlass,
        DateTimeOffset eroeffnetAm,
        DateTimeOffset geaendertAm)
    {
        Id = id;
        Wer = subject;
        Firma = company;
        State = state;
        Anlass = anlass;
        EroeffnetAm = eroeffnetAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Welches Gespräch.</summary>
    public Guid Id { get; }

    /// <summary>Mit wem.</summary>
    public SubjectId Wer { get; }

    /// <summary>Welches Unternehmen.</summary>
    public TenantId Firma { get; }

    /// <summary>Wo es steht.</summary>
    public ConversationState State { get; private set; }

    /// <summary>Was das Unternehmen beim Eröffnen geschrieben hat.</summary>
    public string Anlass { get; }

    /// <summary>Wann es begann.</summary>
    public DateTimeOffset EroeffnetAm { get; }

    /// <summary>Wann sich zuletzt etwas änderte.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary>Läuft es noch?</summary>
    public bool Laeuft => ConversationStates.Laufende.Contains(State);

    /// <summary>Ein Unternehmen eröffnet ein Gespräch.</summary>
    /// <exception cref="Eingabefehler">Der Anlass ist zu lang.</exception>
    public static Conversation Eroeffne(
        SubjectId subject, TenantId company, string? anlass, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, ConversationState.Laufend,
            Text(anlass), now, now);

    /// <summary>Das Gespräch, wie eine Zeile es hält.</summary>
    public static Conversation Stelle_her(
        Guid id,
        SubjectId subject,
        TenantId company,
        ConversationState state,
        string anlass,
        DateTimeOffset eroeffnetAm,
        DateTimeOffset geaendertAm) =>
        new(id, subject, company, state, anlass, eroeffnetAm, geaendertAm);

    /// <summary>Die Person ist einverstanden.</summary>
    /// <remarks>
    /// Nur die Person, und das ist der ganze Punkt: die Zustimmung, aus der ein
    /// Case wird, gehört dem Menschen, um den es geht.
    /// </remarks>
    public void Stimme_zu(SubjectId durch, DateTimeOffset now)
    {
        Meins(durch);
        Genau(ConversationState.Laufend, "agreed to");
        Nach(ConversationState.Zugestimmt, now);
    }

    /// <summary>
    /// Das Unternehmen macht daraus einen Case.
    /// </summary>
    /// <remarks>
    /// <strong>Erst nach der Zustimmung der Person</strong> — und deshalb ist
    /// die Übergabe ein Schritt des Unternehmens: der Case in
    /// transfer-service beginnt damit, dass ein Unternehmen Interesse zeigt,
    /// und dort greifen dann dessen eigene Bedingungen (Marktfreigabe,
    /// Ansprechbarkeit). Hier wird davon nichts wiederholt.
    /// </remarks>
    public void Uebergib(DateTimeOffset now)
    {
        Genau(ConversationState.Zugestimmt, "handed over");
        Nach(ConversationState.Uebergeben, now);
    }

    /// <summary>Jemand beendet das Gespräch.</summary>
    /// <remarks>
    /// Aus jedem laufenden State, von beiden Seiten. Ein Verfahren, aus dem man
    /// nicht aussteigen kann, ist kein Verfahren, sondern eine Falle.
    /// </remarks>
    public void Beende(DateTimeOffset now)
    {
        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(State, "ended");
        }

        Nach(ConversationState.Beendet, now);
    }

    private static string Text(string? value)
    {
        var bereinigt = (value ?? string.Empty).Trim();

        return bereinigt.Length > HoechstlaengeAnlass
            ? throw new Eingabefehler(
                $"the opening note exceeds {HoechstlaengeAnlass} characters")
            : bereinigt;
    }

    private void Meins(SubjectId akteur)
    {
        if (akteur != Wer)
        {
            throw new NichtDeins();
        }
    }

    private void Genau(ConversationState gewollt, string was)
    {
        if (State != gewollt)
        {
            throw new UebergangNichtErlaubt(State, was);
        }
    }

    private void Nach(ConversationState target, DateTimeOffset now)
    {
        State = target;
        GeaendertAm = now;
    }
}

/// <summary>Findet und speichert Gespräche.</summary>
public interface IConversationStore
{
    /// <summary>Ein Gespräch, oder <c>null</c>.</summary>
    Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Das laufende Gespräch zwischen diesen beiden, falls es eines gibt.</summary>
    /// <remarks>
    /// Genau eines je (Mensch, Unternehmen) — sonst stünden zwei Stufenstände
    /// nebeneinander, die dieselben Fähigkeiten meinen.
    /// </remarks>
    Task<Conversation?> GetRunningAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>Die Gespräche dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<Conversation>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Die Gespräche dieses Unternehmens, neueste zuerst.</summary>
    Task<IReadOnlyList<Conversation>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Alles, was dieser Dienst über einen Menschen hält.</summary>
    /// <returns>Wie viel absichtlich stehen blieb. Immer null.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
