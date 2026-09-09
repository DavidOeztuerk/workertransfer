using Girder.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Markt;

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
public abstract class Marktfehler(string meldung) : Exception(meldung);

/// <summary>Die Notiz ist zu lang.</summary>
public sealed class Notizfehler(int grenze)
    : Marktfehler($"The note exceeds {grenze} characters");

/// <summary>Wie ansprechbar jemand ist.</summary>
/// <remarks>
/// Drei Zustände, <strong>alle Übergänge erlaubt</strong>. Es ist eine Aussage
/// über den eigenen Willen, und der ändert sich ohne Reihenfolge. Eine
/// Zustandsmaschine mit Verboten wäre hier Bevormundung: niemand muss erst
/// „offen" gewesen sein, um „zuhörend" zu werden.
/// </remarks>
public enum Verfuegbarkeit
{
    /// <summary>Sucht.</summary>
    Open,

    /// <summary>Hört zu.</summary>
    Listening,

    /// <summary>Nicht ansprechbar.</summary>
    Unavailable
}

/// <summary>Die Worte, mit denen eine Verfügbarkeit auf der Leitung steht.</summary>
public static class Verfuegbarkeiten
{
    /// <summary>Das Wort zum Zustand.</summary>
    public static string Wort(Verfuegbarkeit wert) => wert switch
    {
        Verfuegbarkeit.Open => "open",
        Verfuegbarkeit.Listening => "listening",
        Verfuegbarkeit.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(wert))
    };

    /// <summary>Der Zustand zum Wort, oder <c>null</c>.</summary>
    public static Verfuegbarkeit? Lies(string? wort) => wort switch
    {
        "open" => Verfuegbarkeit.Open,
        "listening" => Verfuegbarkeit.Listening,
        "unavailable" => Verfuegbarkeit.Unavailable,
        _ => null
    };
}

/// <summary>Der Marktstatus einer Person — „bin ich ansprechbar?"</summary>
/// <remarks>
/// <strong>Die gefährlichste Angabe im ganzen System.</strong> Ein Lebenslauf
/// verrät, wo jemand war; der Marktstatus verrät, dass er weg will — und schon
/// die <em>Existenz</em> der Aussage kann jemanden den Arbeitsplatz kosten.
/// <para>
/// Deshalb gibt es bewusst <strong>kein <c>:public</c></strong>: die Freigabe
/// nennt immer einen Empfänger. Beim Profil ist „für alle Unternehmen" eine
/// sinnvolle Wahl; hier wäre sie ein Schalter, dessen Folgen niemand
/// überblickt — darunter der eigene Arbeitgeber, der auf derselben Plattform
/// ist.
/// </para>
/// </remarks>
public sealed class Marktstatus
{
    /// <summary>Wie lang die Notiz sein darf.</summary>
    public const int HoechstlaengeNotiz = 500;

    private Marktstatus(
        SubjectId wer,
        Verfuegbarkeit verfuegbarkeit,
        bool beschaeftigt,
        string notiz,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Wer = wer;
        Verfuegbarkeit = verfuegbarkeit;
        Beschaeftigt = beschaeftigt;
        Notiz = notiz;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Wessen Status. Er <em>ist</em> der Schlüssel.</summary>
    public SubjectId Wer { get; }

    /// <summary>Wie ansprechbar.</summary>
    public Verfuegbarkeit Verfuegbarkeit { get; private set; }

    /// <summary>Arbeite ich gerade irgendwo?</summary>
    /// <remarks>
    /// Ein Feld, kein Zustand: es ist keine Absicht. Man kann beschäftigt
    /// <em>und</em> offen sein — das ist der Normalfall auf einem
    /// Transfermarkt, und als Zustand wäre er unmöglich.
    /// </remarks>
    public bool Beschaeftigt { get; private set; }

    /// <summary>Was die Person dazu schreibt.</summary>
    public string Notiz { get; private set; }

    /// <summary>Wann er angelegt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann er zuletzt geändert wurde.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary>Darf man diese Person ansprechen?</summary>
    /// <remarks>
    /// <c>Unavailable</c> heißt nein — auch für ein Unternehmen mit Freigabe.
    /// <strong>Die Freigabe erlaubt zu sehen, nicht zu stören.</strong>
    /// </remarks>
    public bool Ansprechbar =>
        Verfuegbarkeit is Verfuegbarkeit.Open or Verfuegbarkeit.Listening;

    /// <summary>Wer nichts gesagt hat, hat nicht „ich höre zu" gesagt.</summary>
    /// <remarks>
    /// Die Voreinstellung darf nie zugunsten des Marktes ausfallen.
    /// </remarks>
    public static Marktstatus Voreingestellt(SubjectId wer, DateTimeOffset jetzt) =>
        new(wer, Verfuegbarkeit.Unavailable, beschaeftigt: false, string.Empty, jetzt, jetzt);

    /// <summary>Legt einen Status an.</summary>
    /// <exception cref="Notizfehler">Die Notiz ist zu lang.</exception>
    public static Marktstatus Lege_an(
        SubjectId wer,
        Verfuegbarkeit verfuegbarkeit,
        bool beschaeftigt,
        string notiz,
        DateTimeOffset jetzt) =>
        new(wer, verfuegbarkeit, beschaeftigt, Text(notiz), jetzt, jetzt);

    /// <summary>Der Status, wie eine Zeile ihn hält.</summary>
    public static Marktstatus Stelle_her(
        SubjectId wer,
        Verfuegbarkeit verfuegbarkeit,
        bool beschaeftigt,
        string notiz,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm) =>
        new(wer, verfuegbarkeit, beschaeftigt, notiz, angelegtAm, geaendertAm);

    /// <summary>Schreibt den Status neu.</summary>
    /// <exception cref="Notizfehler">Die Notiz ist zu lang.</exception>
    public void Aendere(
        Verfuegbarkeit verfuegbarkeit,
        bool beschaeftigt,
        string notiz,
        DateTimeOffset jetzt)
    {
        // Erst prüfen, dann schreiben: ein abgelehntes Formular darf kein halb
        // geändertes Aggregat hinterlassen.
        var geprueft = Text(notiz);

        Verfuegbarkeit = verfuegbarkeit;
        Beschaeftigt = beschaeftigt;
        Notiz = geprueft;
        GeaendertAm = jetzt;
    }

    private static string Text(string? notiz)
    {
        var bereinigt = (notiz ?? string.Empty).Trim();

        return bereinigt.Length > HoechstlaengeNotiz
            ? throw new Notizfehler(HoechstlaengeNotiz)
            : bereinigt;
    }
}

/// <summary>Findet und speichert Marktstatus.</summary>
public interface IMarktspeicher
{
    /// <summary>Der Status dieser Person, oder <c>null</c>.</summary>
    Task<Marktstatus?> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Marktstatus status, CancellationToken cancellationToken = default);
}
