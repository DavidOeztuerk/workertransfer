using Girder.Core.Identity;

namespace WorkerTransfer.Assessment.Domain.Vorgaenge;

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
/// <remarks>
/// Die Meldung nennt die <em>Regel</em> und nie den Wert. Der Text einer
/// Aufgabe oder einer Bewertung stünde sonst am Ende im Protokoll — und beide
/// handeln von einem Menschen.
/// </remarks>
/// <param name="meldung">Was die Regel verlangt.</param>
public sealed class Eingabefehler(string meldung) : Exception(meldung);

/// <summary>Von hier aus geht dieser Schritt nicht.</summary>
/// <remarks>
/// Getrennt vom <see cref="Eingabefehler"/>, weil es etwas anderes ist: die
/// Eingabe war in Ordnung, der Vorgang steht nur woanders. Der Endpunkt macht
/// daraus 409 und nicht 422 — „das geht jetzt nicht" ist keine Aussage über das
/// Formular.
/// </remarks>
/// <param name="meldung">Warum nicht.</param>
public sealed class SchrittNichtMoeglich(string meldung) : Exception(meldung);

/// <summary>Es gibt hier nichts zu sehen.</summary>
/// <remarks>
/// <para><strong>Ein Ausgang für vier Lagen:</strong> den Vorgang gibt es
/// nicht; er gehört jemand anderem; er gehört einem anderen Unternehmen; die
/// Person hat diesem Unternehmen nichts (mehr) freigegeben. Alle vier antworten
/// 404, bis auf die Korrelationskennung byte-identisch (ADR-0020 §1).</para>
///
/// <para>Ein eigener Code für die letzte wäre der „gesperrt"-Hinweis — und
/// verriete genau das, was nicht freigegeben ist: dass es etwas gibt.</para>
/// </remarks>
public sealed class KeinVorgang() : Exception("No such assessment");

/// <summary>Wo ein Vorgang steht.</summary>
/// <remarks>
/// <para><strong>Vier Werte, und keiner davon heisst „abgelehnt".</strong> Wer
/// eine Aufgabe nicht annehmen will, tut nichts; die Frist läuft ab, und der
/// Vorgang steht auf <see cref="Abgelaufen"/>. Dieser Stand ist
/// ununterscheidbar von „hat es sich vorgenommen und nicht geschafft" und von
/// „war krank" — er sagt nur, dass nichts eingereicht wurde (ADR-0042 §3).</para>
///
/// <para><strong>Er wird nicht gespeichert, sondern gerechnet</strong> — aus
/// Einreichung, Bewertung und der Uhr. Eine Spalte müsste jemand umschalten,
/// wenn eine Frist abläuft, und das wäre ein Nachtlauf, der Vorgänge über
/// Menschen anfasst, ohne dass jemand gefragt hat.</para>
/// </remarks>
public enum Stand
{
    /// <summary>Gestellt, nichts eingereicht, Frist läuft.</summary>
    Gestellt,

    /// <summary>Eingereicht, noch nicht bewertet.</summary>
    Eingereicht,

    /// <summary>Bewertet — und die Person liest die Bewertung.</summary>
    Bewertet,

    /// <summary>Die Frist ist verstrichen, ohne dass etwas kam.</summary>
    Abgelaufen
}

/// <summary>Wie das Unternehmen die Arbeit beantwortet.</summary>
/// <remarks>
/// <para>Zwei Werte, und ausdrücklich <strong>keine Zahl</strong>: keine Note,
/// keine Sterne, kein Prozentwert, kein „3 von 5" (ADR-0042 §1). Eine Zahl
/// verbirgt genau das, was hilft — was am Ergebnis fehlte —, und sie sieht
/// dabei aus wie eine Messung.</para>
///
/// <para>Sie handelt vom <em>Vorgang</em> und nicht vom Menschen: „wir machen
/// weiter" oder „wir machen nicht weiter". Es gibt keine Stelle, an der solche
/// Ausgänge über Vorgänge hinweg gezählt werden, und keinen Endpunkt, der sie
/// über Unternehmensgrenzen herausgibt.</para>
/// </remarks>
public enum Ausgang
{
    /// <summary>Es geht weiter.</summary>
    Angenommen,

    /// <summary>Es geht nicht weiter — und die Begründung steht daneben.</summary>
    Abgelehnt
}

/// <summary>Die Worte, mit denen Stand und Ausgang auf der Leitung stehen.</summary>
public static class Woerter
{
    /// <summary>Das Wort zum Stand.</summary>
    public static string Wort(Stand stand) => stand switch
    {
        Stand.Gestellt => "set",
        Stand.Eingereicht => "submitted",
        Stand.Bewertet => "evaluated",
        Stand.Abgelaufen => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(stand))
    };

    /// <summary>Das Wort zum Ausgang.</summary>
    public static string Wort(Ausgang ausgang) => ausgang switch
    {
        Ausgang.Angenommen => "accepted",
        Ausgang.Abgelehnt => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(ausgang))
    };

    /// <summary>Der Ausgang zum Wort, oder <c>null</c>.</summary>
    /// <remarks>
    /// <c>LiesAusgang</c> und nicht <c>Ausgang</c>: eine Methode, die heisst wie
    /// ihr Rückgabetyp, verdeckt ihn im ganzen Namensraum.
    /// </remarks>
    public static Ausgang? LiesAusgang(string? wort) => wort switch
    {
        "accepted" => Ausgang.Angenommen,
        "rejected" => Ausgang.Abgelehnt,
        _ => null
    };
}

/// <summary>Die Aufgabe selbst — und die zwei Zahlen, die sie begrenzen.</summary>
/// <remarks>
/// <para><strong><see cref="Stunden"/> ist Pflicht, und das ist der ganze Punkt
/// dieses Dienstes.</strong> Eine Arbeitsprobe ohne genannten Umfang ist eine
/// Aufgabe, deren Preis die Person erst kennt, wenn sie ihn bezahlt hat. Der
/// Umfang steht vorne, damit sie entscheiden kann.</para>
///
/// <para>Eine Zahl ist hier also ausdrücklich erlaubt — sie handelt von der
/// <em>Aufgabe</em>. Verboten sind Zahlen über den Menschen, und der
/// Unterschied ist die ganze Trennlinie von ADR-0022: Anforderung rein, Belege
/// raus; niemals Mensch rein, Zahl raus.</para>
/// </remarks>
public sealed record Aufgabe
{
    /// <summary>Die Obergrenze des Umfangs, in Stunden.</summary>
    /// <remarks>
    /// Acht, und das ist eine Entscheidung: mehr als ein Arbeitstag ist keine
    /// Probe mehr, sondern Arbeit — und für Arbeit gibt es einen Vertrag und
    /// kein Formular. Die Plattform bietet für „sechzehn Stunden" schlicht kein
    /// Feld an. Wer so etwas will, muss es ausserhalb tun, und dann ist es
    /// sichtbar das, was es ist (ADR-0042 §3).
    /// </remarks>
    public const int HoechsterUmfang = 8;

    /// <summary>Und die Untergrenze.</summary>
    /// <remarks>
    /// Eine Stunde. Nicht null: „kostet dich nichts" ist keine Angabe, sondern
    /// eine Beschwichtigung, und sie wäre die erste, die jemand einträgt.
    /// </remarks>
    public const int KleinsterUmfang = 1;

    /// <summary>Wie weit die Frist mindestens entfernt liegt.</summary>
    /// <remarks>
    /// Achtundvierzig Stunden. Eine Aufgabe für morgen früh misst nicht, wie
    /// jemand arbeitet, sondern ob er gerade alles andere stehen lassen kann —
    /// und das hat mit Können nichts zu tun und mit Lebensumständen alles. Es
    /// ist dieselbe Schieflage, die ADR-0022 an GitHub beschreibt.
    /// </remarks>
    public static readonly TimeSpan Mindestfrist = TimeSpan.FromHours(48);

    /// <summary>Wie lang die Überschrift sein darf.</summary>
    public const int HoechstlaengeTitel = 200;

    /// <summary>Wie lang die Aufgabenstellung sein darf.</summary>
    public const int HoechstlaengeText = 8000;

    private Aufgabe(string titel, string text, int stunden, DateTimeOffset frist)
    {
        Titel = titel;
        Text = text;
        Stunden = stunden;
        Frist = frist;
    }

    /// <summary>Die Überschrift.</summary>
    public string Titel { get; }

    /// <summary>Was zu tun ist.</summary>
    public string Text { get; }

    /// <summary>Der genannte Umfang in Stunden, 1 bis 8.</summary>
    public int Stunden { get; }

    /// <summary>Bis wann.</summary>
    public DateTimeOffset Frist { get; }

    /// <summary>Prüft jede Angabe und baut erst dann.</summary>
    /// <exception cref="Eingabefehler">Eine Angabe hält die Regel nicht ein.</exception>
    public static Aufgabe Schreibe(
        string? titel, string? text, int stunden, DateTimeOffset frist, DateTimeOffset jetzt)
    {
        var ueberschrift = Pflichttext(titel, HoechstlaengeTitel, "a title");
        var aufgabe = Pflichttext(text, HoechstlaengeText, "a task description");

        if (stunden is < KleinsterUmfang or > HoechsterUmfang)
        {
            throw new Eingabefehler(
                $"the stated effort is between {KleinsterUmfang} and {HoechsterUmfang} hours");
        }

        if (frist - jetzt < Mindestfrist)
        {
            throw new Eingabefehler(
                $"a deadline lies at least {Mindestfrist.TotalHours:0} hours ahead");
        }

        return new Aufgabe(ueberschrift, aufgabe, stunden, frist);
    }

    /// <summary>Die Aufgabe, wie eine Zeile sie hält — ohne erneute Prüfung.</summary>
    /// <remarks>
    /// Eine gespeicherte Zeile war bei ihrer Entstehung gültig. Sie beim Lesen
    /// abzulehnen hiesse, jemandem seinen Vorgang zu entziehen, weil sich eine
    /// Grenze geändert hat.
    /// </remarks>
    public static Aufgabe Stelle_her(
        string titel, string text, int stunden, DateTimeOffset frist) =>
        new(titel, text, stunden, frist);

    private static string Pflichttext(string? roh, int grenze, string was)
    {
        var wert = (roh ?? string.Empty).Trim();

        return wert.Length switch
        {
            0 => throw new Eingabefehler($"{was} is required"),
            var laenge when laenge > grenze =>
                throw new Eingabefehler($"{was} is at most {grenze} characters"),
            _ => wert
        };
    }
}

/// <summary>Was die Person abgegeben hat.</summary>
/// <remarks>
/// <para><strong>Text und höchstens eine Adresse — nie Bytes.</strong> Dateien
/// einer Person liegen in portfolio-service und in resume-service, und beide
/// räumen sie in der Kaskade ab. Eine dritte Ablage wäre ein dritter Ort, den
/// eine Löschung erreichen muss — und der eine, den sie irgendwann nicht
/// erreicht (ADR-0042).</para>
///
/// <para><strong>Die Adresse wird nie abgerufen.</strong> Sie wird gespeichert
/// und angezeigt, und ein Mensch klickt sie. Ein Dienst, der sie holte, führte
/// eine von einem Fremden gewählte Adresse in eine Serveranfrage — und die
/// Egress-Grenze wiese sie ohnehin ab, weil sie in keiner Konfiguration steht.
/// Das ist hier kein Hindernis, sondern die richtige Antwort.</para>
/// </remarks>
public sealed record Einreichung
{
    /// <summary>Wie lang der Begleittext sein darf.</summary>
    public const int HoechstlaengeText = 8000;

    /// <summary>Wie lang eine Adresse sein darf.</summary>
    public const int HoechstlaengeAdresse = 2000;

    private Einreichung(string text, string? adresse, DateTimeOffset am)
    {
        Text = text;
        Adresse = adresse;
        Am = am;
    }

    /// <summary>Was die Person dazu geschrieben hat. Darf leer sein.</summary>
    public string Text { get; }

    /// <summary>Wohin sie zeigt, oder <c>null</c>.</summary>
    public string? Adresse { get; }

    /// <summary>Wann.</summary>
    public DateTimeOffset Am { get; }

    /// <summary>Prüft und baut.</summary>
    /// <remarks>
    /// Eines von beiden muss da sein. Eine leere Einreichung wäre gar keine —
    /// und sie stünde als „eingereicht" da, was gegenüber der Person die
    /// falsche Zusage und gegenüber dem Unternehmen die falsche Auskunft wäre.
    /// </remarks>
    /// <exception cref="Eingabefehler">Eine Angabe hält die Regel nicht ein.</exception>
    public static Einreichung Schreibe(string? text, string? adresse, DateTimeOffset jetzt)
    {
        var begleit = (text ?? string.Empty).Trim();
        var ziel = Adressform((adresse ?? string.Empty).Trim());

        if (begleit.Length == 0 && ziel is null)
        {
            throw new Eingabefehler("a submission carries a text or a link");
        }

        if (begleit.Length > HoechstlaengeText)
        {
            throw new Eingabefehler($"a submission text is at most {HoechstlaengeText} characters");
        }

        return new Einreichung(begleit, ziel, jetzt);
    }

    /// <summary>Die Einreichung, wie eine Zeile sie hält.</summary>
    public static Einreichung Stelle_her(string text, string? adresse, DateTimeOffset am) =>
        new(text, adresse, am);

    private static string? Adressform(string roh)
    {
        if (roh.Length == 0)
        {
            return null;
        }

        if (roh.Length > HoechstlaengeAdresse)
        {
            throw new Eingabefehler($"a link is at most {HoechstlaengeAdresse} characters");
        }

        // Nur http und https, und nur als absolute Adresse. `javascript:` und
        // `data:` stuenden sonst als Verweis in einer fremden Oberflaeche —
        // gespeichert von einem Menschen, geklickt von einem anderen.
        return Uri.TryCreate(roh, UriKind.Absolute, out var adresse)
               && (adresse.Scheme == Uri.UriSchemeHttp || adresse.Scheme == Uri.UriSchemeHttps)
            ? adresse.ToString()
            : throw new Eingabefehler("a link is an absolute http or https address");
    }
}

/// <summary>Die Rückmeldung des Unternehmens.</summary>
/// <remarks>
/// <para><strong>Ein Feld, und die Person liest genau dieses.</strong> Es gibt
/// kein internes Feld daneben, keine Notiz, kein „nur für uns". Wo es zwei
/// gäbe, stünde im zweiten die Wahrheit (ADR-0042 §2).</para>
///
/// <para><strong>Der Text ist Pflicht, auch bei einer Absage.</strong> Ablehnen
/// und Begründen sind ein Schritt und nicht zwei — der zweite liesse sich sonst
/// weglassen, und genau das ist der Tausch, den dieser Dienst nicht vermitteln
/// darf: Arbeit gegen Schweigen.</para>
/// </remarks>
public sealed record Bewertung
{
    /// <summary>Wie lang die Rückmeldung sein darf.</summary>
    public const int HoechstlaengeText = 8000;

    private Bewertung(string text, Ausgang ausgang, DateTimeOffset am)
    {
        Text = text;
        Ausgang = ausgang;
        Am = am;
    }

    /// <summary>Was das Unternehmen geschrieben hat. Nie leer.</summary>
    public string Text { get; }

    /// <summary>Ob es weitergeht.</summary>
    public Ausgang Ausgang { get; }

    /// <summary>Wann.</summary>
    public DateTimeOffset Am { get; }

    /// <summary>Prüft und baut.</summary>
    /// <exception cref="Eingabefehler">Der Text fehlt oder ist zu lang.</exception>
    public static Bewertung Schreibe(string? text, Ausgang ausgang, DateTimeOffset jetzt)
    {
        var wort = (text ?? string.Empty).Trim();

        return wort.Length switch
        {
            0 => throw new Eingabefehler("an evaluation carries a text, a rejection too"),
            var laenge when laenge > HoechstlaengeText =>
                throw new Eingabefehler($"an evaluation is at most {HoechstlaengeText} characters"),
            _ => new Bewertung(wort, ausgang, jetzt)
        };
    }

    /// <summary>Die Bewertung, wie eine Zeile sie hält.</summary>
    public static Bewertung Stelle_her(string text, Ausgang ausgang, DateTimeOffset am) =>
        new(text, ausgang, am);
}

/// <summary>Ein Vorgang: eine Aufgabe, eine Einreichung, eine Bewertung.</summary>
/// <remarks>
/// <para><strong>Die Bewertung gehört diesem Aggregat und sonst nichts</strong>
/// (ADR-0042 §1). Sie steht in keiner Suche, in keinem Profil, und kein zweites
/// Unternehmen sieht sie — nicht weil eine Prüfung das abwehrt, sondern weil es
/// keinen Weg dorthin gibt: der Bestand kennt zwei Fragen, „die Vorgänge dieser
/// Firma" und „meine Vorgänge", und eine dritte („die Vorgänge dieser Person",
/// gestellt von einer Firma) existiert nicht.</para>
///
/// <para><strong>Bewertet wird einmal.</strong> Ein zweites Mal ist 409. Eine
/// Bewertung, die sich nachträglich ändern lässt, ist eine, die die Person
/// gelesen haben kann, bevor sie ihren endgültigen Wortlaut bekam.</para>
/// </remarks>
public sealed class Vorgang
{
    private Vorgang(
        Guid id,
        SubjectId wer,
        TenantId firma,
        Aufgabe aufgabe,
        Einreichung? einreichung,
        Bewertung? bewertung,
        DateTimeOffset gestelltAm,
        DateTimeOffset geaendertAm)
    {
        Id = id;
        Wer = wer;
        Firma = firma;
        Aufgabe = aufgabe;
        Einreichung = einreichung;
        Bewertung = bewertung;
        GestelltAm = gestelltAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Welcher Vorgang.</summary>
    public Guid Id { get; }

    /// <summary>Wem die Aufgabe gestellt wurde.</summary>
    public SubjectId Wer { get; }

    /// <summary>Welches Unternehmen sie gestellt hat.</summary>
    public TenantId Firma { get; }

    /// <summary>Was zu tun war.</summary>
    public Aufgabe Aufgabe { get; }

    /// <summary>Was abgegeben wurde, oder <c>null</c>.</summary>
    public Einreichung? Einreichung { get; private set; }

    /// <summary>Was zurückkam, oder <c>null</c>.</summary>
    public Bewertung? Bewertung { get; private set; }

    /// <summary>Wann die Aufgabe gestellt wurde.</summary>
    public DateTimeOffset GestelltAm { get; }

    /// <summary>Wann sich zuletzt etwas bewegt hat.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary>Wo der Vorgang zu diesem Zeitpunkt steht.</summary>
    /// <remarks>
    /// Gerechnet, nicht gespeichert. Die Reihenfolge der Fragen ist die
    /// Aussage: eine abgelaufene Frist macht einen eingereichten Vorgang nicht
    /// wieder zunichte — wer geliefert hat, hat geliefert, und das Unternehmen
    /// schuldet die Antwort auch danach noch.
    /// </remarks>
    public Stand Stand_am(DateTimeOffset jetzt)
    {
        if (Bewertung is not null)
        {
            return Stand.Bewertet;
        }

        if (Einreichung is not null)
        {
            return Stand.Eingereicht;
        }

        return jetzt > Aufgabe.Frist ? Stand.Abgelaufen : Stand.Gestellt;
    }

    /// <summary>Stellt eine Aufgabe.</summary>
    public static Vorgang Stelle(
        SubjectId wer, TenantId firma, Aufgabe aufgabe, DateTimeOffset jetzt) =>
        new(Guid.NewGuid(), wer, firma, aufgabe, null, null, jetzt, jetzt);

    /// <summary>Der Vorgang, wie eine Zeile ihn hält.</summary>
    public static Vorgang Stelle_her(
        Guid id,
        SubjectId wer,
        TenantId firma,
        Aufgabe aufgabe,
        Einreichung? einreichung,
        Bewertung? bewertung,
        DateTimeOffset gestelltAm,
        DateTimeOffset geaendertAm) =>
        new(id, wer, firma, aufgabe, einreichung, bewertung, gestelltAm, geaendertAm);

    /// <summary>Die Person reicht ein.</summary>
    /// <remarks>
    /// Nach der Frist geht es nicht mehr, und das schützt beide Seiten: eine
    /// Aufgabe ohne Ende ist eine, die in jemandes Kopf für immer offen bleibt.
    /// </remarks>
    /// <exception cref="SchrittNichtMoeglich">Schon eingereicht, oder Frist vorbei.</exception>
    public void Reiche_ein(Einreichung einreichung, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(einreichung);

        if (Einreichung is not null)
        {
            throw new SchrittNichtMoeglich("this assessment has already been submitted");
        }

        if (jetzt > Aufgabe.Frist)
        {
            throw new SchrittNichtMoeglich("the deadline for this assessment has passed");
        }

        Einreichung = einreichung;
        GeaendertAm = jetzt;
    }

    /// <summary>Das Unternehmen antwortet.</summary>
    /// <remarks>
    /// Ohne Einreichung gibt es nichts zu bewerten — und eine Bewertung ohne
    /// Arbeit wäre eine Aussage über den Menschen statt über seine Arbeit.
    /// </remarks>
    /// <exception cref="SchrittNichtMoeglich">Nichts eingereicht, oder schon bewertet.</exception>
    public void Bewerte(Bewertung bewertung, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(bewertung);

        if (Einreichung is null)
        {
            throw new SchrittNichtMoeglich("there is nothing submitted to evaluate");
        }

        if (Bewertung is not null)
        {
            throw new SchrittNichtMoeglich("this assessment has already been evaluated");
        }

        Bewertung = bewertung;
        GeaendertAm = jetzt;
    }
}

/// <summary>Findet und speichert Vorgänge.</summary>
/// <remarks>
/// <para><strong>Zwei Fragen, und keine dritte.</strong>
/// <see cref="FuerFirmaAsync"/> trägt den Mandanten aus dem geprüften Token;
/// <see cref="FuerPersonAsync"/> ist die eigene Liste eines Menschen. Eine
/// Methode „die Vorgänge dieser Person", von einer Firma aufrufbar, wäre die
/// Auskunftei aus ADR-0042 §1 — und sie gibt es hier nicht, damit sie niemand
/// vorfindet.</para>
/// </remarks>
public interface IVorgangsspeicher
{
    /// <summary>Ein Vorgang, oder <c>null</c>.</summary>
    Task<Vorgang?> HoleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Alles, was einer Person gestellt wurde — ihre eigene Liste.</summary>
    Task<IReadOnlyList<Vorgang>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Alles, was ein Unternehmen gestellt hat.</summary>
    Task<IReadOnlyList<Vorgang>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Vorgang vorgang, CancellationToken cancellationToken = default);

    /// <summary>Löscht alles über eine Person.</summary>
    /// <returns>Wie viel absichtlich stehen blieb. Immer null.</returns>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
