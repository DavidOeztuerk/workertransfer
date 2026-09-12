using Girder.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Vorgaenge;

/// <summary>Wo ein Vorgang steht.</summary>
public enum Transferstand
{
    /// <summary>Das Unternehmen hat Interesse gezeigt.</summary>
    Interested,

    /// <summary>Man spricht.</summary>
    Talking,

    /// <summary>Es liegt ein Angebot vor.</summary>
    Offered,

    /// <summary>Die Person hat angenommen.</summary>
    Accepted,

    /// <summary>Abgeschlossen.</summary>
    Completed,

    /// <summary>Von der Person abgesagt.</summary>
    Declined,

    /// <summary>Vom Unternehmen zurückgezogen.</summary>
    Withdrawn
}

/// <summary>Die Worte, mit denen ein Transferstand auf der Leitung steht.</summary>
public static class Transferstaende
{
    /// <summary>Das Wort zum Stand.</summary>
    public static string Wort(Transferstand stand) => stand switch
    {
        Transferstand.Interested => "interested",
        Transferstand.Talking => "talking",
        Transferstand.Offered => "offered",
        Transferstand.Accepted => "accepted",
        Transferstand.Completed => "completed",
        Transferstand.Declined => "declined",
        Transferstand.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(stand))
    };

    /// <summary>Der Stand zum Wort, oder <c>null</c>.</summary>
    public static Transferstand? Lies(string? wort) => wort switch
    {
        "interested" => Transferstand.Interested,
        "talking" => Transferstand.Talking,
        "offered" => Transferstand.Offered,
        "accepted" => Transferstand.Accepted,
        "completed" => Transferstand.Completed,
        "declined" => Transferstand.Declined,
        "withdrawn" => Transferstand.Withdrawn,
        _ => null
    };

    /// <summary>Die Stände, in denen ein Vorgang noch läuft.</summary>
    /// <remarks>
    /// Steht hier und nicht nur im Aggregat, weil der Teilindex in der
    /// Datenbank dieselbe Menge braucht: genau ein laufender Vorgang je
    /// (Person, Unternehmen).
    /// </remarks>
    public static readonly IReadOnlyList<Transferstand> Laufende =
    [
        Transferstand.Interested,
        Transferstand.Talking,
        Transferstand.Offered,
        Transferstand.Accepted
    ];
}

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
public abstract class Vorgangsfehler(string meldung) : Exception(meldung);

/// <summary>Ein Textfeld ist zu lang.</summary>
public sealed class Textfehler(string feld, int grenze)
    : Vorgangsfehler($"{feld} exceeds {grenze} characters");

/// <summary>Mit dem Angebot stimmt etwas nicht.</summary>
public sealed class Angebotsfehler(string was) : Vorgangsfehler(was);

/// <summary>Das ist der Vorgang eines anderen Menschen.</summary>
public sealed class NichtDeiner() : Vorgangsfehler("This transfer belongs to someone else");

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(Transferstand jetzt, string was)
    : Vorgangsfehler($"A {Transferstaende.Wort(jetzt)} transfer cannot be {was}");

/// <summary>Der Transfer-Vorgang — drei Ja, jederzeit ein Nein.</summary>
/// <remarks>
/// Der ULTRAPLAN verlangt „beschäftigt → Firma muss mitwirken". Das lässt sich
/// so nicht bauen: <strong>die Plattform weiß nicht, wo jemand arbeitet.</strong>
/// <c>Beschaeftigt</c> ist ein Wahrheitswert, den die Person selbst setzt; es
/// gibt keinen Datensatz „Anna arbeitet bei X".
/// <para>
/// Und es soll keinen geben. Er wäre die Verbindung zwischen „arbeitet bei X"
/// und „hört zu" — genau die Auskunft, die jemanden den Arbeitsplatz kostet, in
/// einer einzigen Tabelle. Ihn anzulegen, damit die Plattform den Arbeitgeber
/// anschreiben kann, hieße, das größte Risiko des Systems zu erzeugen, um eine
/// Höflichkeit zu ermöglichen.
/// </para>
/// <para>
/// Stattdessen: der Vorgang trägt, dass eine Freigabe <em>nötig</em> ist, und
/// die Person selbst bestätigt, dass sie vorliegt. Das ist schwächer (niemand
/// prüft es) und sicherer. Zwischen einer Zusicherung, die niemand einlösen
/// kann, und einer, die niemanden gefährdet, ist die zweite die ehrlichere.
/// </para>
/// </remarks>
public sealed class Transfer
{
    /// <summary>Wie lang Nachricht und Angebotstext sein dürfen.</summary>
    public const int HoechstlaengeText = 2000;

    private Transfer(
        Guid id,
        SubjectId wer,
        TenantId firma,
        Transferstand stand,
        bool brauchtFreigabe,
        bool freigabeBestaetigt,
        string nachricht,
        string angebotstext,
        string? angebotsbeginn,
        long? angebotsgebuehrCent,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Id = id;
        Wer = wer;
        Firma = firma;
        Stand = stand;
        BrauchtFreigabe = brauchtFreigabe;
        FreigabeBestaetigt = freigabeBestaetigt;
        Nachricht = nachricht;
        Angebotstext = angebotstext;
        Angebotsbeginn = angebotsbeginn;
        AngebotsgebuehrCent = angebotsgebuehrCent;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Welcher Vorgang.</summary>
    public Guid Id { get; }

    /// <summary>Um wen es geht.</summary>
    public SubjectId Wer { get; }

    /// <summary>Welches Unternehmen.</summary>
    public TenantId Firma { get; }

    /// <summary>Wo er steht.</summary>
    public Transferstand Stand { get; private set; }

    /// <summary>Ob eine Freigabe des jetzigen Arbeitgebers nötig ist.</summary>
    /// <remarks>
    /// Beim Anlegen aus dem Marktstatus kopiert, nicht bei jedem Lesezugriff
    /// neu geholt: wer während eines laufenden Gesprächs kündigt, ändert damit
    /// nicht rückwirkend die Bedingungen eines Angebots — und ein Unternehmen
    /// kann nicht darauf hoffen, dass sich die Regel noch ändert.
    /// </remarks>
    public bool BrauchtFreigabe { get; }

    /// <summary>Von der <em>Person</em> bestätigt.</summary>
    /// <remarks>Die Plattform prüft es nicht und kann es nicht.</remarks>
    public bool FreigabeBestaetigt { get; private set; }

    /// <summary>Was das Unternehmen geschrieben hat.</summary>
    public string Nachricht { get; }

    /// <summary>Was im Angebot steht.</summary>
    public string Angebotstext { get; private set; }

    /// <summary>Wann es losgehen soll, oder <c>null</c>.</summary>
    public string? Angebotsbeginn { get; private set; }

    /// <summary>Die vereinbarte Ablöse in Cent, oder <c>null</c>.</summary>
    /// <remarks>
    /// Festgehalten, nicht bewegt: die Plattform führt kein Geld. Es ist eine
    /// Zahl, auf die sich zwei Unternehmen einigen, und sie steht hier, damit
    /// beide Seiten dieselbe im Blick haben.
    /// </remarks>
    public long? AngebotsgebuehrCent { get; private set; }

    /// <summary>Wann er begann.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann sich zuletzt etwas änderte.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary>Läuft er noch?</summary>
    /// <remarks>
    /// Endgültig heißt endgültig. Wer erneut will, beginnt einen neuen
    /// Vorgang — und braucht dafür wieder eine gültige Freigabe des
    /// Marktstatus.
    /// </remarks>
    public bool Laeuft => Transferstaende.Laufende.Contains(Stand);

    /// <summary>Ein Unternehmen zeigt Interesse.</summary>
    public static Transfer Zeige_Interesse(
        SubjectId wer,
        TenantId firma,
        bool brauchtFreigabe,
        string nachricht,
        DateTimeOffset jetzt) =>
        new(Guid.CreateVersion7(), wer, firma, Transferstand.Interested, brauchtFreigabe,
            freigabeBestaetigt: false, Text("Message", nachricht), string.Empty,
            null, null, jetzt, jetzt);

    /// <summary>Der Vorgang, wie eine Zeile ihn hält.</summary>
    public static Transfer Stelle_her(
        Guid id,
        SubjectId wer,
        TenantId firma,
        Transferstand stand,
        bool brauchtFreigabe,
        bool freigabeBestaetigt,
        string nachricht,
        string angebotstext,
        string? angebotsbeginn,
        long? angebotsgebuehrCent,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm) =>
        new(id, wer, firma, stand, brauchtFreigabe, freigabeBestaetigt, nachricht,
            angebotstext, angebotsbeginn, angebotsgebuehrCent, angelegtAm, geaendertAm);

    // --- Die Person ------------------------------------------------------

    /// <summary>Die Person lässt sich auf ein Gespräch ein.</summary>
    public void Nimm_Gespraech_an(SubjectId durch, DateTimeOffset jetzt)
    {
        Meiner(durch);
        Genau(Transferstand.Interested, "opened for talks");
        Nach(Transferstand.Talking, jetzt);
    }

    /// <summary>Die Person nimmt das Angebot an.</summary>
    public void Nimm_Angebot_an(SubjectId durch, DateTimeOffset jetzt)
    {
        Meiner(durch);
        Genau(Transferstand.Offered, "accepted");
        Nach(Transferstand.Accepted, jetzt);
    }

    /// <summary>Die Person bestätigt, dass ihr Arbeitgeber sie gehen lässt.</summary>
    /// <remarks>
    /// Niemand prüft das, und niemand kann es: die Plattform kennt weder den
    /// Arbeitgeber noch den Vertrag. Was der Schritt leistet, ist, die Frage zu
    /// stellen und die Antwort festzuhalten.
    /// </remarks>
    public void Bestaetige_Freigabe(SubjectId durch, DateTimeOffset jetzt)
    {
        Meiner(durch);
        Genau(Transferstand.Accepted, "released");

        if (!BrauchtFreigabe)
        {
            throw new UebergangNichtErlaubt(Stand, "released without needing a release");
        }

        FreigabeBestaetigt = true;
        Nach(Transferstand.Completed, jetzt);
    }

    /// <summary>Die Person sagt ab.</summary>
    /// <remarks>
    /// Immer möglich, aus jedem laufenden Zustand. Ein Verfahren, aus dem man
    /// nicht aussteigen kann, ist kein Verfahren, sondern eine Falle.
    /// </remarks>
    public void Sage_ab(SubjectId durch, DateTimeOffset jetzt)
    {
        Meiner(durch);
        Laufend("declined");
        Nach(Transferstand.Declined, jetzt);
    }

    // --- Das Unternehmen -------------------------------------------------

    /// <summary>Das Unternehmen macht ein Angebot.</summary>
    public void Mache_Angebot(
        string text, string? beginn, long? gebuehrCent, DateTimeOffset jetzt)
    {
        Genau(Transferstand.Talking, "offered");

        if (gebuehrCent is < 0)
        {
            throw new Angebotsfehler("A transfer fee cannot be negative");
        }

        Angebotstext = Text("Offer", text);
        Angebotsbeginn = beginn;
        AngebotsgebuehrCent = gebuehrCent;
        Nach(Transferstand.Offered, jetzt);
    }

    /// <summary>Das Unternehmen schließt ab.</summary>
    /// <remarks>
    /// Der Abschluss ist die Aussage „wir stellen ein" — die trifft der
    /// Arbeitgeber. Die Person hat mit <see cref="Nimm_Angebot_an"/> bereits ja
    /// gesagt.
    /// </remarks>
    public void Schliesse_ab(DateTimeOffset jetzt)
    {
        Genau(Transferstand.Accepted, "completed");

        if (BrauchtFreigabe)
        {
            // Solange eine Freigabe nötig ist, schließt die Person ab — sie ist
            // die Einzige, die weiß, ob sie vorliegt.
            throw new UebergangNichtErlaubt(Stand, "completed before the release is confirmed");
        }

        Nach(Transferstand.Completed, jetzt);
    }

    /// <summary>Das Unternehmen zieht zurück.</summary>
    public void Ziehe_zurueck(DateTimeOffset jetzt)
    {
        Laufend("withdrawn");
        Nach(Transferstand.Withdrawn, jetzt);
    }

    // --- Gemeinsames -----------------------------------------------------

    private static string Text(string feld, string? wert)
    {
        var bereinigt = (wert ?? string.Empty).Trim();

        return bereinigt.Length > HoechstlaengeText
            ? throw new Textfehler(feld, HoechstlaengeText)
            : bereinigt;
    }

    private void Meiner(SubjectId akteur)
    {
        if (akteur != Wer)
        {
            throw new NichtDeiner();
        }
    }

    private void Genau(Transferstand gewollt, string was)
    {
        if (Stand != gewollt)
        {
            throw new UebergangNichtErlaubt(Stand, was);
        }
    }

    private void Laufend(string was)
    {
        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(Stand, was);
        }
    }

    private void Nach(Transferstand ziel, DateTimeOffset jetzt)
    {
        Stand = ziel;
        GeaendertAm = jetzt;
    }
}

/// <summary>Findet und speichert Vorgänge.</summary>
public interface ITransferspeicher
{
    /// <summary>Ein Vorgang, oder <c>null</c>.</summary>
    Task<Transfer?> HoleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Der laufende Vorgang zwischen diesen beiden, falls es ihn gibt.</summary>
    Task<Transfer?> HoleLaufendenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Transfer vorgang, CancellationToken cancellationToken = default);

    /// <summary>Die Vorgänge dieser Person.</summary>
    Task<IReadOnlyList<Transfer>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Die Vorgänge dieses Unternehmens.</summary>
    Task<IReadOnlyList<Transfer>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default);
}
