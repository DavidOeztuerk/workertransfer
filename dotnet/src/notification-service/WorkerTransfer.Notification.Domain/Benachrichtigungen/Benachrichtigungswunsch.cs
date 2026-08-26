using Girder.Core.Identity;

namespace WorkerTransfer.Notification.Domain.Benachrichtigungen;

/// <summary>Vier Schalter und eine Drossel, alles an der Person.</summary>
/// <remarks>
/// Die Drossel steht hier und nicht in einer eigenen Tabelle: es gibt genau
/// eine je Person, sie ist kein eigener Gegenstand, und eine zweite Tabelle
/// wäre ein Verbund für einen Zeitstempel.
/// </remarks>
public sealed class Benachrichtigungswunsch
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

    private Benachrichtigungswunsch(
        SubjectId wer,
        Dictionary<Benachrichtigungsart, bool> arten,
        DateTimeOffset? zuletztGesendetAm)
    {
        Wer = wer;
        _arten = arten;
        ZuletztGesendetAm = zuletztGesendetAm;
    }

    /// <summary>Wessen Wünsche. Sie <em>sind</em> der Schlüssel.</summary>
    public SubjectId Wer { get; }

    /// <summary>Wann zuletzt etwas hinausging, oder <c>null</c>.</summary>
    public DateTimeOffset? ZuletztGesendetAm { get; private set; }

    /// <summary>Alle an.</summary>
    /// <remarks>
    /// Eine Benachrichtigung über den <em>eigenen</em> Vorgang ist keine
    /// Werbung, sondern die Bedingung dafür, dass „die Person entscheidet"
    /// überhaupt eintreten kann. Wer nichts eingestellt hat, soll erfahren, dass
    /// jemand etwas von ihm will.
    /// </remarks>
    public static Benachrichtigungswunsch Voreingestellt(SubjectId wer) =>
        new(wer, Benachrichtigungsarten.Alle.ToDictionary(art => art, _ => true), null);

    /// <summary>Die Wünsche, wie eine Zeile sie hält.</summary>
    public static Benachrichtigungswunsch Stelle_her(
        SubjectId wer,
        IReadOnlyDictionary<Benachrichtigungsart, bool> arten,
        DateTimeOffset? zuletztGesendetAm) =>
        new(wer, new Dictionary<Benachrichtigungsart, bool>(arten), zuletztGesendetAm);

    /// <summary>Will die Person Post dieser Art?</summary>
    /// <remarks>
    /// Eine unbekannte Art gilt als gewollt: eine neue Art soll nicht
    /// stillschweigend ausbleiben, weil eine alte Zeile sie nicht kennt.
    /// </remarks>
    public bool Will(Benachrichtigungsart art) => _arten.GetValueOrDefault(art, true);

    /// <summary>Setzt einen Schalter.</summary>
    public void Setze(Benachrichtigungsart art, bool gewollt) => _arten[art] = gewollt;

    /// <summary>Darf jetzt etwas hinausgehen?</summary>
    public bool Darf(Benachrichtigungsart art, DateTimeOffset jetzt)
    {
        if (!Will(art))
        {
            return false;
        }

        return ZuletztGesendetAm is not { } zuletzt || jetzt - zuletzt >= Drossel;
    }

    /// <summary>Merkt sich, dass etwas hinausging.</summary>
    public void MerkeGesendet(DateTimeOffset jetzt) => ZuletztGesendetAm = jetzt;

    /// <summary>Die Schalter, zum Schreiben in eine Zeile.</summary>
    public IReadOnlyDictionary<Benachrichtigungsart, bool> Schalter => _arten;
}

/// <summary>Ein Eintrag im Postfach der Person.</summary>
/// <remarks>
/// <strong>Kein Inhalt.</strong> Dieselbe Regel wie bei der Outbox, und aus
/// demselben Grund: was hier steht, landet in jeder Sicherung. Eine Spalte für
/// den Text wäre eine Einladung, „Acme GmbH möchte deinen Marktstatus sehen"
/// hineinzuschreiben. Die Art genügt, um in der Anwendung die richtige Stelle
/// zu zeigen — und die Anwendung prüft, wer liest.
/// </remarks>
public sealed class Eingang
{
    private Eingang(
        Guid id,
        SubjectId wer,
        Benachrichtigungsart art,
        DateTimeOffset angelegtAm,
        DateTimeOffset? gelesenAm)
    {
        Id = id;
        Wer = wer;
        Art = art;
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
    public static Eingang Lege_an(SubjectId wer, Benachrichtigungsart art, DateTimeOffset jetzt) =>
        new(Guid.CreateVersion7(), wer, art, jetzt, null);

    /// <summary>Der Eintrag, wie eine Zeile ihn hält.</summary>
    public static Eingang Stelle_her(
        Guid id,
        SubjectId wer,
        Benachrichtigungsart art,
        DateTimeOffset angelegtAm,
        DateTimeOffset? gelesenAm) =>
        new(id, wer, art, angelegtAm, gelesenAm);

    /// <summary>Markiert ihn als gelesen. Ein zweites Mal ändert nichts.</summary>
    public void Lies(DateTimeOffset jetzt) => GelesenAm ??= jetzt;
}

/// <summary>Findet und speichert Wünsche.</summary>
public interface IWunschspeicher
{
    /// <summary>Die Wünsche dieser Person, oder <c>null</c>.</summary>
    Task<Benachrichtigungswunsch?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(
        Benachrichtigungswunsch wunsch, CancellationToken cancellationToken = default);
}

/// <summary>Findet und speichert Eingänge.</summary>
public interface IEingangsspeicher
{
    /// <summary>Legt einen Eintrag an.</summary>
    Task FuegeHinzuAsync(Eingang eingang, CancellationToken cancellationToken = default);

    /// <summary>Das Postfach dieser Person, neueste zuerst.</summary>
    Task<IReadOnlyList<Eingang>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Markiert alles Ungelesene als gelesen.</summary>
    /// <returns>Wie viele Einträge das betraf.</returns>
    Task<int> LiesAllesAsync(
        SubjectId wer, DateTimeOffset jetzt, CancellationToken cancellationToken = default);
}
