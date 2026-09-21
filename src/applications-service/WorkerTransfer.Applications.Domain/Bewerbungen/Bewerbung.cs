using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Domain.Applications;

/// <summary>Wo eine JobApplication steht.</summary>
public enum ApplicationState
{
    /// <summary>Abgeschickt.</summary>
    Submitted,

    /// <summary>Das Unternehmen sieht sie an.</summary>
    Reviewing,

    /// <summary>Abgelehnt.</summary>
    Rejected,

    /// <summary>Von der Person zurückgezogen.</summary>
    Withdrawn,

    /// <summary>Eingestellt.</summary>
    Hired
}

/// <summary>Etwas an der Eingabe stimmt nicht.</summary>
public abstract class Eingabefehler(string notification) : Exception(notification);

/// <summary>Die Nachricht ist zu lang.</summary>
public sealed class NachrichtFehler(int limit)
    : Eingabefehler($"Die Nachricht ist länger als {limit} Zeichen.");

/// <summary>Das ist die JobApplication eines anderen Menschen.</summary>
public sealed class NichtDeine() : Eingabefehler("Diese Bewerbung gehört jemand anderem.");

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(ApplicationState now, ApplicationState gewollt)
    : Eingabefehler($"Eine {now}-Bewerbung kann nicht {gewollt} werden.");

/// <summary>Was mitgeschickt wird.</summary>
/// <remarks>
/// Das Profile ist immer dabei und lässt sich nicht abwählen: eine JobApplication
/// ohne jede Angabe zur Person ist keine, und „ich bewerbe mich, aber ihr dürft
/// nichts von mir sehen" ist keine Wahl, die jemand ernsthaft trifft.
/// </remarks>
public sealed record Mitgeschicktes(
    bool CurriculumVitae = false,
    bool Portfolio = false,
    IReadOnlyList<Guid>? Unterlagen = null)
{
    /// <summary>Immer wahr. Kein Feld, damit es niemand auf falsch setzt.</summary>
    public bool Profile => true;

    /// <summary>Welche Unterlagen beiliegen — die Kennungen, in ihrer Reihenfolge.</summary>
    /// <remarks>
    /// <strong>Eine Momentaufnahme, kein Verweis auf „alles, was sie hat".</strong>
    /// Wer später eine DocumentRecord hochlädt, hat sie dieser JobApplication nicht
    /// beigelegt; wer eine löscht, hat sie trotzdem geschickt. Die Liste hier
    /// sagt, was tatsächlich hinausging.
    /// </remarks>
    public IReadOnlyList<Guid> Unterlagen { get; init; } = Unterlagen ?? [];
}

/// <summary>Eine JobApplication.</summary>
/// <remarks>
/// Sie gehört <em>beiden</em> — der Person und dem Unternehmen. Deshalb hat
/// jede Seite eigene Wege: die Person zieht zurück, das Unternehmen bewegt sie
/// durch das Verfahren, und keiner der beiden darf, was der andere darf.
/// <para>
/// Es gibt hier keinen Punktwert und keine Rangfolge. Eine Liste von
/// Applications, die nach irgendetwas sortiert wäre, das der Dienst gerechnet
/// hat, ist die Kandidatenbewertung durch die Hintertür (ADR-0022).
/// </para>
/// </remarks>
public sealed class JobApplication
{
    /// <summary>Wie lang die Nachricht sein darf.</summary>
    public const int HoechstlaengeNachricht = 4000;

    /// <summary>
    /// Zustände, aus denen es keinen Weg zurück in ein laufendes Verfahren gibt.
    /// </summary>
    /// <remarks>
    /// Nach einer Ablehnung erneut abzuschicken wäre Nachfassen gegen ein
    /// „nein", das schon gefallen ist — dieselbe Regel wie beim CurriculumVitae.
    /// </remarks>
    private static readonly ApplicationState[] Endgueltig =
        [ApplicationState.Rejected, ApplicationState.Hired];

    private JobApplication(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string message,
        Mitgeschicktes mitgeschickt,
        ApplicationState state,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        DateTimeOffset? beantwortetAm,
        ApplicantContact kontakt)
    {
        Id = id;
        Job = job;
        Firma = company;
        Wer = subject;
        Nachricht = message;
        Mitgeschickt = mitgeschickt;
        State = state;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
        BeantwortetAm = beantwortetAm;
        Kontakt = kontakt;
    }

    /// <summary>Welche JobApplication.</summary>
    public Guid Id { get; }

    /// <summary>Auf welche Job.</summary>
    public Guid Job { get; }

    /// <summary>
    /// Welches Unternehmen — aus der Job kopiert, mit Absicht.
    /// </summary>
    /// <remarks>
    /// Ein Fremdschlüssel geht nicht (andere Datenbank, ADR-0004), und ein
    /// Aufruf je Lesezugriff wäre teuer für eine Angabe, die sich nie ändert:
    /// eine Job wechselt nicht das Unternehmen. Eine Kopie ist nur
    /// gefährlich, wenn das Original sich ändern kann.
    /// </remarks>
    public TenantId Firma { get; }

    /// <summary>Wer sich beworben hat.</summary>
    public SubjectId Wer { get; }

    /// <summary>Was die Person geschrieben hat.</summary>
    public string Nachricht { get; private set; }

    /// <summary>Was sie mitgeschickt hat.</summary>
    public Mitgeschicktes Mitgeschickt { get; private set; }

    /// <summary>Wo sie steht.</summary>
    public ApplicationState State { get; private set; }

    /// <summary>Wann sie abgeschickt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann sich zuletzt etwas änderte.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary><c>null</c>, solange nicht entschieden.</summary>
    public DateTimeOffset? BeantwortetAm { get; private set; }

    /// <summary>
    /// Briefkopf zum Zeitpunkt des Sendens — Klarname, PostalAddress, Telefon, Mail.
    /// </summary>
    /// <remarks>
    /// Snapshot, nicht Verweis (ADR-0038): ein späterer Umzug darf die
    /// Firmenmappe nicht umschreiben. Nie im CoverLetterContext, nie an ein Modell.
    /// </remarks>
    public ApplicantContact Kontakt { get; private set; }

    /// <summary>
    /// Läuft sie noch — und damit die Release der Daten?
    /// </summary>
    /// <remarks>
    /// Der Grund, warum das eine Frage an die JobApplication ist und nicht an eine
    /// Sichtbarkeitsspalte: solange sie läuft, darf das Unternehmen sehen, was
    /// mitgeschickt wurde. Danach nicht mehr, und niemand muss daran denken,
    /// etwas zurückzunehmen.
    /// </remarks>
    public bool Laeuft => State is ApplicationState.Submitted or ApplicationState.Reviewing;

    /// <summary>Schickt eine JobApplication ab.</summary>
    public static JobApplication Schicke_ab(
        Guid job,
        TenantId company,
        SubjectId subject,
        string message,
        Mitgeschicktes mitgeschickt,
        DateTimeOffset now,
        ApplicantContact? kontakt = null) =>
        new(Guid.CreateVersion7(), job, company, subject, Text(message), mitgeschickt,
            ApplicationState.Submitted, now, now, null,
            kontakt ?? ApplicantContact.Leer);

    /// <summary>Die JobApplication, wie eine Zeile sie hält.</summary>
    public static JobApplication Stelle_her(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string message,
        Mitgeschicktes mitgeschickt,
        ApplicationState state,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        DateTimeOffset? beantwortetAm,
        ApplicantContact? kontakt = null) =>
        new(id, job, company, subject, message, mitgeschickt, state,
            angelegtAm, geaendertAm, beantwortetAm, kontakt ?? ApplicantContact.Leer);

    /// <summary>Nach einem Rückzug erneut bewerben — eine neue Entscheidung.</summary>
    /// <remarks>
    /// Nach einer Ablehnung nicht: das wäre Nachfassen gegen ein „nein", das
    /// schon gefallen ist.
    /// </remarks>
    /// <exception cref="UebergangNichtErlaubt">Sie wurde nicht zurückgezogen.</exception>
    public void Schicke_erneut(
        string message,
        Mitgeschicktes mitgeschickt,
        DateTimeOffset now,
        ApplicantContact? kontakt = null)
    {
        if (State is not ApplicationState.Withdrawn)
        {
            throw new UebergangNichtErlaubt(State, ApplicationState.Submitted);
        }

        Nachricht = Text(message);
        Mitgeschickt = mitgeschickt;
        Kontakt = kontakt ?? ApplicantContact.Leer;
        State = ApplicationState.Submitted;
        BeantwortetAm = null;
        GeaendertAm = now;
    }

    /// <summary>Zieht sie zurück.</summary>
    /// <remarks>
    /// Immer möglich, solange sie läuft — auch aus <c>Reviewing</c>: wer nicht
    /// mehr will, muss nicht warten, bis jemand anderes fertig ist.
    /// </remarks>
    /// <exception cref="NichtDeine">Sie gehört jemand anderem.</exception>
    /// <exception cref="UebergangNichtErlaubt">Sie läuft nicht mehr.</exception>
    public void Ziehe_zurueck(SubjectId durch, DateTimeOffset now)
    {
        if (durch != Wer)
        {
            throw new NichtDeine();
        }

        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(State, ApplicationState.Withdrawn);
        }

        State = ApplicationState.Withdrawn;
        GeaendertAm = now;
    }

    /// <summary>Das Unternehmen bewegt sie durch das Verfahren.</summary>
    /// <exception cref="UebergangNichtErlaubt">Von hier aus geht das nicht.</exception>
    public void Bewege(ApplicationState target, DateTimeOffset now)
    {
        if (target is not (ApplicationState.Reviewing or ApplicationState.Rejected
                         or ApplicationState.Hired))
        {
            // Ein Unternehmen zieht keine JobApplication zurück — das ist die
            // Handlung der Person, und sie hier zuzulassen hieße, es in ihrem
            // Namen tun zu können.
            throw new UebergangNichtErlaubt(State, target);
        }

        // Eine zurückgezogene JobApplication ist keine mehr; eine abgelehnte oder
        // angenommene ist entschieden.
        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(State, target);
        }

        State = target;

        if (Endgueltig.Contains(target))
        {
            BeantwortetAm = now;
        }

        GeaendertAm = now;
    }

    private static string Text(string? message)
    {
        var bereinigt = (message ?? string.Empty).Trim();

        return bereinigt.Length > HoechstlaengeNachricht
            ? throw new NachrichtFehler(HoechstlaengeNachricht)
            : bereinigt;
    }
}

/// <summary>
/// Briefkopf zum Sendezeitpunkt. Gehört der Mappe, nicht dem Prompt (ADR-0038).
/// </summary>
public sealed record ApplicantContact(
    string Klarname,
    string Zeile1,
    string Zeile2,
    string Postleitzahl,
    string Ort,
    string Land,
    string Telefon,
    string Email)
{
    /// <summary>Nichts hinterlegt — der Briefkopf bleibt leer, das Senden nicht.</summary>
    public static ApplicantContact Leer { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, "DE", string.Empty, string.Empty);
}

/// <summary>Findet und speichert Applications.</summary>
public interface IApplicationStore
{
    /// <summary>Eine JobApplication, oder <c>null</c>.</summary>
    Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Die JobApplication dieser Person auf diese Job, falls es sie gibt.</summary>
    /// <remarks>
    /// Eine Person bewirbt sich einmal je Job. Ein zweites Mal ist entweder
    /// dieselbe JobApplication erneut geschickt oder gar nicht erlaubt — nie eine
    /// zweite Zeile.
    /// </remarks>
    Task<JobApplication?> GetAsync(
        Guid job, SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SaveAsync(JobApplication application, CancellationToken cancellationToken = default);

    /// <summary>Die Applications einer Person, neueste zuerst.</summary>
    Task<IReadOnlyList<JobApplication>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Die Applications auf eine Job dieses Unternehmens.</summary>
    Task<IReadOnlyList<JobApplication>> ForJobAsync(
        Guid job, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Wie viele Applications dieses Unternehmen in welchem State hat.</summary>
    Task<IReadOnlyDictionary<ApplicationState, int>> CountAsync(
        TenantId company, CancellationToken cancellationToken = default);
}

/// <summary>Die Worte, mit denen ein State auf der Leitung und in der Spalte steht.</summary>
/// <remarks>
/// An einer Job, weil es zwei Ränder gibt, die dasselbe Wort brauchen — die
/// Spalte <c>applications.status</c> und das Feld <c>status</c> im Vertrag. Zwei
/// Übersetzungen wären zwei Gelegenheiten, sich zu unterscheiden, und die
/// Oberfläche prüft die Worte einzeln ab.
/// </remarks>
public static class ApplicationStates
{
    /// <summary>Das Wort zum State.</summary>
    public static string Wort(ApplicationState state) => state switch
    {
        ApplicationState.Submitted => "submitted",
        ApplicationState.Reviewing => "reviewing",
        ApplicationState.Rejected => "rejected",
        ApplicationState.Withdrawn => "withdrawn",
        ApplicationState.Hired => "hired",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>Der State zum Wort, oder <c>null</c>.</summary>
    public static ApplicationState? Lies(string? word) => word switch
    {
        "submitted" => ApplicationState.Submitted,
        "reviewing" => ApplicationState.Reviewing,
        "rejected" => ApplicationState.Rejected,
        "withdrawn" => ApplicationState.Withdrawn,
        "hired" => ApplicationState.Hired,
        _ => null
    };
}
