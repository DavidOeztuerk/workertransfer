using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Vorgaenge;

/// <summary>Wo ein Case steht.</summary>
public enum TransferState
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

/// <summary>Die Worte, mit denen ein TransferState auf der Leitung steht.</summary>
public static class Transferstaende
{
    /// <summary>Das Wort zum State.</summary>
    public static string Wort(TransferState state) => state switch
    {
        TransferState.Interested => "interested",
        TransferState.Talking => "talking",
        TransferState.Offered => "offered",
        TransferState.Accepted => "accepted",
        TransferState.Completed => "completed",
        TransferState.Declined => "declined",
        TransferState.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>Der State zum Wort, oder <c>null</c>.</summary>
    public static TransferState? Lies(string? word) => word switch
    {
        "interested" => TransferState.Interested,
        "talking" => TransferState.Talking,
        "offered" => TransferState.Offered,
        "accepted" => TransferState.Accepted,
        "completed" => TransferState.Completed,
        "declined" => TransferState.Declined,
        "withdrawn" => TransferState.Withdrawn,
        _ => null
    };

    /// <summary>Die Stände, in denen ein Case noch läuft.</summary>
    /// <remarks>
    /// Steht hier und nicht nur im Aggregat, weil der Teilindex in der
    /// Datenbank dieselbe Menge braucht: genau ein laufender Case je
    /// (Person, Unternehmen).
    /// </remarks>
    public static readonly IReadOnlyList<TransferState> Laufende =
    [
        TransferState.Interested,
        TransferState.Talking,
        TransferState.Offered,
        TransferState.Accepted
    ];
}

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
public abstract class CaseError(string notification) : Exception(notification);

/// <summary>Ein Textfeld ist zu lang.</summary>
public sealed class Textfehler(string field, int limit)
    : CaseError($"{field} exceeds {limit} characters");

/// <summary>Mit dem Angebot stimmt etwas nicht.</summary>
public sealed class Angebotsfehler(string was) : CaseError(was);

/// <summary>Das ist der Case eines anderen Menschen.</summary>
public sealed class NichtDeiner() : CaseError("This transfer belongs to someone else");

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(TransferState now, string was)
    : CaseError($"A {Transferstaende.Wort(now)} transfer cannot be {was}");

/// <summary>Der Transfer-Case — drei Ja, jederzeit ein Nein.</summary>
/// <remarks>
/// Der ULTRAPLAN verlangt „beschäftigt → Firma muss mitwirken". Das lässt sich
/// so nicht bauen: <strong>die Plattform weiß nicht, wo jemand arbeitet.</strong>
/// <c>Beschaeftigt</c> ist ein Wahrheitswert, den die Person selbst setzt; es
/// gibt keinen Datensatz „Anna arbeitet bei X".
/// <para>
/// Und es soll keinen geben. Er wäre die Connection zwischen „arbeitet bei X"
/// und „hört zu" — genau die Auskunft, die jemanden den Arbeitsplatz kostet, in
/// einer einzigen Tabelle. Ihn anzulegen, damit die Plattform den Arbeitgeber
/// anschreiben kann, hieße, das größte Risiko des Systems zu erzeugen, um eine
/// Höflichkeit zu ermöglichen.
/// </para>
/// <para>
/// Stattdessen: der Case trägt, dass eine Release <em>nötig</em> ist, und
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
        SubjectId subject,
        TenantId company,
        TransferState state,
        bool brauchtFreigabe,
        bool freigabeBestaetigt,
        string message,
        string angebotstext,
        string? angebotsbeginn,
        long? angebotsgebuehrCent,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Id = id;
        Wer = subject;
        Firma = company;
        State = state;
        BrauchtFreigabe = brauchtFreigabe;
        FreigabeBestaetigt = freigabeBestaetigt;
        Nachricht = message;
        Angebotstext = angebotstext;
        Angebotsbeginn = angebotsbeginn;
        AngebotsgebuehrCent = angebotsgebuehrCent;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Welcher Case.</summary>
    public Guid Id { get; }

    /// <summary>Um wen es geht.</summary>
    public SubjectId Wer { get; }

    /// <summary>Welches Unternehmen.</summary>
    public TenantId Firma { get; }

    /// <summary>Wo er steht.</summary>
    public TransferState State { get; private set; }

    /// <summary>Ob eine Release des jetzigen Arbeitgebers nötig ist.</summary>
    /// <remarks>
    /// Beim Anlegen aus dem MarketStatus kopiert, nicht bei jedem Lesezugriff
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
    /// Case — und braucht dafür wieder eine gültige Release des
    /// MarketStatus.
    /// </remarks>
    public bool Laeuft => Transferstaende.Laufende.Contains(State);

    /// <summary>Ein Unternehmen zeigt Interesse.</summary>
    public static Transfer Zeige_Interesse(
        SubjectId subject,
        TenantId company,
        bool brauchtFreigabe,
        string message,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, TransferState.Interested, brauchtFreigabe,
            freigabeBestaetigt: false, Text("Message", message), string.Empty,
            null, null, now, now);

    /// <summary>Der Case, wie eine Zeile ihn hält.</summary>
    public static Transfer Stelle_her(
        Guid id,
        SubjectId subject,
        TenantId company,
        TransferState state,
        bool brauchtFreigabe,
        bool freigabeBestaetigt,
        string message,
        string angebotstext,
        string? angebotsbeginn,
        long? angebotsgebuehrCent,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm) =>
        new(id, subject, company, state, brauchtFreigabe, freigabeBestaetigt, message,
            angebotstext, angebotsbeginn, angebotsgebuehrCent, angelegtAm, geaendertAm);

    // --- Die Person ------------------------------------------------------

    /// <summary>Die Person lässt sich auf ein Gespräch ein.</summary>
    public void Nimm_Gespraech_an(SubjectId durch, DateTimeOffset now)
    {
        Meiner(durch);
        Genau(TransferState.Interested, "opened for talks");
        Nach(TransferState.Talking, now);
    }

    /// <summary>Die Person nimmt das Angebot an.</summary>
    public void Nimm_Angebot_an(SubjectId durch, DateTimeOffset now)
    {
        Meiner(durch);
        Genau(TransferState.Offered, "accepted");
        Nach(TransferState.Accepted, now);
    }

    /// <summary>Die Person bestätigt, dass ihr Arbeitgeber sie gehen lässt.</summary>
    /// <remarks>
    /// Niemand prüft das, und niemand kann es: die Plattform kennt weder den
    /// Arbeitgeber noch den Vertrag. Was der Schritt leistet, ist, die Frage zu
    /// stellen und die Antwort festzuhalten.
    /// </remarks>
    public void Bestaetige_Freigabe(SubjectId durch, DateTimeOffset now)
    {
        Meiner(durch);
        Genau(TransferState.Accepted, "released");

        if (!BrauchtFreigabe)
        {
            throw new UebergangNichtErlaubt(State, "released without needing a release");
        }

        FreigabeBestaetigt = true;
        Nach(TransferState.Completed, now);
    }

    /// <summary>Die Person sagt ab.</summary>
    /// <remarks>
    /// Immer möglich, aus jedem laufenden Zustand. Ein Verfahren, aus dem man
    /// nicht aussteigen kann, ist kein Verfahren, sondern eine Falle.
    /// </remarks>
    public void Sage_ab(SubjectId durch, DateTimeOffset now)
    {
        Meiner(durch);
        Laufend("declined");
        Nach(TransferState.Declined, now);
    }

    // --- Das Unternehmen -------------------------------------------------

    /// <summary>Das Unternehmen macht ein Angebot.</summary>
    public void Mache_Angebot(
        string text, string? beginn, long? gebuehrCent, DateTimeOffset now)
    {
        Genau(TransferState.Talking, "offered");

        if (gebuehrCent is < 0)
        {
            throw new Angebotsfehler("A transfer fee cannot be negative");
        }

        Angebotstext = Text("Offer", text);
        Angebotsbeginn = beginn;
        AngebotsgebuehrCent = gebuehrCent;
        Nach(TransferState.Offered, now);
    }

    /// <summary>Das Unternehmen schließt ab.</summary>
    /// <remarks>
    /// Der Abschluss ist die Aussage „wir stellen ein" — die trifft der
    /// Arbeitgeber. Die Person hat mit <see cref="Nimm_Angebot_an"/> bereits ja
    /// gesagt.
    /// </remarks>
    public void Schliesse_ab(DateTimeOffset now)
    {
        Genau(TransferState.Accepted, "completed");

        if (BrauchtFreigabe)
        {
            // Solange eine Release nötig ist, schließt die Person ab — sie ist
            // die Einzige, die weiß, ob sie vorliegt.
            throw new UebergangNichtErlaubt(State, "completed before the release is confirmed");
        }

        Nach(TransferState.Completed, now);
    }

    /// <summary>Das Unternehmen zieht zurück.</summary>
    public void Ziehe_zurueck(DateTimeOffset now)
    {
        Laufend("withdrawn");
        Nach(TransferState.Withdrawn, now);
    }

    // --- Gemeinsames -----------------------------------------------------

    private static string Text(string field, string? value)
    {
        var bereinigt = (value ?? string.Empty).Trim();

        return bereinigt.Length > HoechstlaengeText
            ? throw new Textfehler(field, HoechstlaengeText)
            : bereinigt;
    }

    private void Meiner(SubjectId akteur)
    {
        if (akteur != Wer)
        {
            throw new NichtDeiner();
        }
    }

    private void Genau(TransferState gewollt, string was)
    {
        if (State != gewollt)
        {
            throw new UebergangNichtErlaubt(State, was);
        }
    }

    private void Laufend(string was)
    {
        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(State, was);
        }
    }

    private void Nach(TransferState target, DateTimeOffset now)
    {
        State = target;
        GeaendertAm = now;
    }
}

/// <summary>Findet und speichert Vorgänge.</summary>
public interface ITransferStore
{
    /// <summary>Ein Case, oder <c>null</c>.</summary>
    Task<Transfer?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Der laufende Case zwischen diesen beiden, falls es ihn gibt.</summary>
    Task<Transfer?> GetCurrentAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(Transfer caseItem, CancellationToken cancellationToken = default);

    /// <summary>Die Vorgänge dieser Person.</summary>
    Task<IReadOnlyList<Transfer>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Die Vorgänge dieses Unternehmens.</summary>
    Task<IReadOnlyList<Transfer>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);
}
