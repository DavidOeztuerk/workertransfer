using Noelia.Core.Identity;

namespace WorkerTransfer.Notification.Domain.Benachrichtigungen;

/// <summary>Fünf Schalter und eine Drossel, alles an der Person.</summary>
/// <remarks>
/// Die Drossel steht hier und nicht in einer eigenen Tabelle: es gibt genau
/// eine je Person, sie ist kein eigener Gegenstand, und eine zweite Tabelle
/// wäre ein Verbund für einen Zeitstempel.
/// </remarks>
public sealed class NotificationPreference
{
    /// <summary>Höchstens eine Nachricht je Person und Stunde, über alle Arten hinweg.</summary>
    /// <remarks>
    /// Auch eine inhaltsfreie Mail verrät ihren Zeitpunkt: wer beobachtet, wann
    /// WorkerTransfer schreibt, sieht, <em>wann</em> etwas passiert. Die Drossel
    /// nimmt der Frequenz die Aussagekraft — und macht den Endpunkt nebenbei
    /// als Werkzeug zum Zuspammen wertlos.
    /// <para>
    /// Eine Stunde ist eine Wahl, keine Ableitung: kürzer drosselt nichts, weil
    /// zwischen zwei Sitzungen ohnehin mehr Zeit liegt; länger verschluckt eine
    /// echte Nachricht.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan Drossel = TimeSpan.FromHours(1);

    private readonly Dictionary<Benachrichtigungsart, bool> _arten;

    private NotificationPreference(
        SubjectId subject,
        Dictionary<Benachrichtigungsart, bool> arten,
        DateTimeOffset? zuletztGesendetAm)
    {
        Wer = subject;
        _arten = arten;
        ZuletztGesendetAm = zuletztGesendetAm;
    }

    /// <summary>Wessen Wünsche. Sie <em>sind</em> der Schlüssel.</summary>
    public SubjectId Wer { get; }

    /// <summary>Wann zuletzt etwas hinausging, oder <c>null</c>.</summary>
    public DateTimeOffset? ZuletztGesendetAm { get; private set; }

    /// <summary>Alle an.</summary>
    /// <remarks>
    /// Eine Benachrichtigung über den <em>eigenen</em> Case ist keine
    /// Werbung, sondern die Bedingung dafür, dass „die Person entscheidet"
    /// überhaupt eintreten kann. Wer nichts eingestellt hat, soll erfahren, dass
    /// jemand etwas von ihm will.
    /// </remarks>
    public static NotificationPreference Voreingestellt(SubjectId subject) =>
        new(subject, Benachrichtigungsarten.Alle.ToDictionary(kind => kind, _ => true), null);

    /// <summary>Die Wünsche, wie eine Zeile sie hält.</summary>
    public static NotificationPreference Stelle_her(
        SubjectId subject,
        IReadOnlyDictionary<Benachrichtigungsart, bool> arten,
        DateTimeOffset? zuletztGesendetAm) =>
        new(subject, new Dictionary<Benachrichtigungsart, bool>(arten), zuletztGesendetAm);

    /// <summary>Will die Person Post dieser Art?</summary>
    /// <remarks>
    /// Eine unbekannte Art gilt als gewollt: eine neue Art soll nicht
    /// stillschweigend ausbleiben, weil eine alte Zeile sie nicht kennt.
    /// </remarks>
    public bool Will(Benachrichtigungsart kind) => _arten.GetValueOrDefault(kind, true);

    /// <summary>Setzt einen Schalter.</summary>
    public void Set(Benachrichtigungsart kind, bool gewollt) => _arten[kind] = gewollt;

    /// <summary>Darf jetzt etwas hinausgehen?</summary>
    public bool Darf(Benachrichtigungsart kind, DateTimeOffset now)
    {
        if (!Will(kind))
        {
            return false;
        }

        return ZuletztGesendetAm is not { } zuletzt || now - zuletzt >= Drossel;
    }

    /// <summary>Merkt sich, dass etwas hinausging.</summary>
    public void MerkeGesendet(DateTimeOffset now) => ZuletztGesendetAm = now;

    /// <summary>Die Schalter, zum Schreiben in eine Zeile.</summary>
    public IReadOnlyDictionary<Benachrichtigungsart, bool> Schalter => _arten;
}

/// <summary>Ein Eintrag im Inbox der Person.</summary>
/// <remarks>
/// <strong>Kein Inhalt.</strong> Dieselbe Regel wie bei der Outbox, und aus
/// demselben Grund: was hier steht, landet in jeder Sicherung. Eine Spalte für
/// den Text wäre eine Einladung, „Acme GmbH möchte deinen MarketStatus sehen"
/// hineinzuschreiben. Die Art genügt, um in der Anwendung die richtige Job
/// zu zeigen — und die Anwendung prüft, wer liest.
/// </remarks>
public sealed class Eingang
{
    private Eingang(
        Guid id,
        SubjectId subject,
        Benachrichtigungsart kind,
        DateTimeOffset angelegtAm,
        DateTimeOffset? gelesenAm)
    {
        Id = id;
        Wer = subject;
        Art = kind;
        AngelegtAm = angelegtAm;
        GelesenAm = gelesenAm;
    }

    /// <summary>Welcher Eintrag.</summary>
    public Guid Id { get; }

    /// <summary>Für wen.</summary>
    public SubjectId Wer { get; }

    /// <summary>Worüber.</summary>
    public Benachrichtigungsart Art { get; }

    /// <summary>Wann er entstand.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary><c>null</c>, solange ungelesen.</summary>
    public DateTimeOffset? GelesenAm { get; private set; }

    /// <summary>Legt einen Eintrag an.</summary>
    public static Eingang Lege_an(SubjectId subject, Benachrichtigungsart kind, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, kind, now, null);

    /// <summary>Der Eintrag, wie eine Zeile ihn hält.</summary>
    public static Eingang Stelle_her(
        Guid id,
        SubjectId subject,
        Benachrichtigungsart kind,
        DateTimeOffset angelegtAm,
        DateTimeOffset? gelesenAm) =>
        new(id, subject, kind, angelegtAm, gelesenAm);

    /// <summary>Markiert ihn als gelesen. Ein zweites Mal ändert nichts.</summary>
    public void Lies(DateTimeOffset now) => GelesenAm ??= now;
}

/// <summary>Findet und speichert Wünsche.</summary>
public interface IPreferenceStore
{
    /// <summary>Die Wünsche dieser Person, oder <c>null</c>.</summary>
    Task<NotificationPreference?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(
        NotificationPreference preference, CancellationToken cancellationToken = default);
}

/// <summary>Findet und speichert Eingänge.</summary>
public interface IInboxStore
{
    /// <summary>Legt einen Eintrag an.</summary>
    Task AppendAsync(Eingang eingang, CancellationToken cancellationToken = default);

    /// <summary>Das Inbox dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<Eingang>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Markiert alles Ungelesene als gelesen.</summary>
    /// <returns>Wie viele Einträge das betraf.</returns>
    Task<int> ReadAllAsync(
        SubjectId subject, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Gab es seit <paramref name="seit"/> schon einen Eintrag dieser Art?</summary>
    /// <remarks>
    /// <para>Die Tageskappe aus ADR-0033, und sie wird aus dem <em>Inbox</em>
    /// beantwortet statt aus einer eigenen Spalte. Das ist der Punkt: eine
    /// Spalte „zuletzt entdeckt am" wäre eine zweite Zeile über die
    /// Sichtbarkeit eines Menschen, und das Inbox hält dieselbe Auskunft
    /// bereits — es gehört ihm.</para>
    ///
    /// <para>Sie trägt nebenbei auch die Mindestens-einmal-Zustellung: eine
    /// zweite Ausgangszeile derselben Art am selben Tag legt nichts Neues an.</para>
    /// </remarks>
    Task<bool> ExistedSinceAsync(
        SubjectId subject,
        Benachrichtigungsart kind,
        DateTimeOffset seit,
        CancellationToken cancellationToken = default);
}
