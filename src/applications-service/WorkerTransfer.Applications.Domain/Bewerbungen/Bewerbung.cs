using Girder.Core.Identity;

namespace WorkerTransfer.Applications.Domain.Bewerbungen;

/// <summary>Wo eine Bewerbung steht.</summary>
public enum Bewerbungsstand
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
public abstract class Eingabefehler(string meldung) : Exception(meldung);

/// <summary>Die Nachricht ist zu lang.</summary>
public sealed class NachrichtFehler(int grenze)
    : Eingabefehler($"Die Nachricht ist länger als {grenze} Zeichen.");

/// <summary>Das ist die Bewerbung eines anderen Menschen.</summary>
public sealed class NichtDeine() : Eingabefehler("Diese Bewerbung gehört jemand anderem.");

/// <summary>Dieser Schritt ist von hier aus nicht möglich.</summary>
public sealed class UebergangNichtErlaubt(Bewerbungsstand jetzt, Bewerbungsstand gewollt)
    : Eingabefehler($"Eine {jetzt}-Bewerbung kann nicht {gewollt} werden.");

/// <summary>Was mitgeschickt wird.</summary>
/// <remarks>
/// Das Profil ist immer dabei und lässt sich nicht abwählen: eine Bewerbung
/// ohne jede Angabe zur Person ist keine, und „ich bewerbe mich, aber ihr dürft
/// nichts von mir sehen" ist keine Wahl, die jemand ernsthaft trifft.
/// </remarks>
public sealed record Mitgeschicktes(
    bool Lebenslauf = false,
    bool Portfolio = false,
    IReadOnlyList<Guid>? Unterlagen = null)
{
    /// <summary>Immer wahr. Kein Feld, damit es niemand auf falsch setzt.</summary>
    public bool Profil => true;

    /// <summary>Welche Unterlagen beiliegen — die Kennungen, in ihrer Reihenfolge.</summary>
    /// <remarks>
    /// <strong>Eine Momentaufnahme, kein Verweis auf „alles, was sie hat".</strong>
    /// Wer später eine Unterlage hochlädt, hat sie dieser Bewerbung nicht
    /// beigelegt; wer eine löscht, hat sie trotzdem geschickt. Die Liste hier
    /// sagt, was tatsächlich hinausging.
    /// </remarks>
    public IReadOnlyList<Guid> Unterlagen { get; init; } = Unterlagen ?? [];
}

/// <summary>Eine Bewerbung.</summary>
/// <remarks>
/// Sie gehört <em>beiden</em> — der Person und dem Unternehmen. Deshalb hat
/// jede Seite eigene Wege: die Person zieht zurück, das Unternehmen bewegt sie
/// durch das Verfahren, und keiner der beiden darf, was der andere darf.
/// <para>
/// Es gibt hier keinen Punktwert und keine Rangfolge. Eine Liste von
/// Bewerbungen, die nach irgendetwas sortiert wäre, das der Dienst gerechnet
/// hat, ist die Kandidatenbewertung durch die Hintertür (ADR-0022).
/// </para>
/// </remarks>
public sealed class Bewerbung
{
    /// <summary>Wie lang die Nachricht sein darf.</summary>
    public const int HoechstlaengeNachricht = 4000;

    /// <summary>
    /// Zustände, aus denen es keinen Weg zurück in ein laufendes Verfahren gibt.
    /// </summary>
    /// <remarks>
    /// Nach einer Ablehnung erneut abzuschicken wäre Nachfassen gegen ein
    /// „nein", das schon gefallen ist — dieselbe Regel wie beim Lebenslauf.
    /// </remarks>
    private static readonly Bewerbungsstand[] Endgueltig =
        [Bewerbungsstand.Rejected, Bewerbungsstand.Hired];

    private Bewerbung(
        Guid id,
        Guid stelle,
        TenantId firma,
        SubjectId wer,
        string nachricht,
        Mitgeschicktes mitgeschickt,
        Bewerbungsstand stand,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        DateTimeOffset? beantwortetAm,
        Bewerbungskontakt kontakt)
    {
        Id = id;
        Stelle = stelle;
        Firma = firma;
        Wer = wer;
        Nachricht = nachricht;
        Mitgeschickt = mitgeschickt;
        Stand = stand;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
        BeantwortetAm = beantwortetAm;
        Kontakt = kontakt;
    }

    /// <summary>Welche Bewerbung.</summary>
    public Guid Id { get; }

    /// <summary>Auf welche Stelle.</summary>
    public Guid Stelle { get; }

    /// <summary>
    /// Welches Unternehmen — aus der Stelle kopiert, mit Absicht.
    /// </summary>
    /// <remarks>
    /// Ein Fremdschlüssel geht nicht (andere Datenbank, ADR-0004), und ein
    /// Aufruf je Lesezugriff wäre teuer für eine Angabe, die sich nie ändert:
    /// eine Stelle wechselt nicht das Unternehmen. Eine Kopie ist nur
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
    public Bewerbungsstand Stand { get; private set; }

    /// <summary>Wann sie abgeschickt wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann sich zuletzt etwas änderte.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary><c>null</c>, solange nicht entschieden.</summary>
    public DateTimeOffset? BeantwortetAm { get; private set; }

    /// <summary>
    /// Briefkopf zum Zeitpunkt des Sendens — Klarname, Anschrift, Telefon, Mail.
    /// </summary>
    /// <remarks>
    /// Snapshot, nicht Verweis (ADR-0038): ein späterer Umzug darf die
    /// Firmenmappe nicht umschreiben. Nie im Anschreibenkontext, nie an ein Modell.
    /// </remarks>
    public Bewerbungskontakt Kontakt { get; private set; }

    /// <summary>
    /// Läuft sie noch — und damit die Freigabe der Daten?
    /// </summary>
    /// <remarks>
    /// Der Grund, warum das eine Frage an die Bewerbung ist und nicht an eine
    /// Sichtbarkeitsspalte: solange sie läuft, darf das Unternehmen sehen, was
    /// mitgeschickt wurde. Danach nicht mehr, und niemand muss daran denken,
    /// etwas zurückzunehmen.
    /// </remarks>
    public bool Laeuft => Stand is Bewerbungsstand.Submitted or Bewerbungsstand.Reviewing;

    /// <summary>Schickt eine Bewerbung ab.</summary>
    public static Bewerbung Schicke_ab(
        Guid stelle,
        TenantId firma,
        SubjectId wer,
        string nachricht,
        Mitgeschicktes mitgeschickt,
        DateTimeOffset jetzt,
        Bewerbungskontakt? kontakt = null) =>
        new(Guid.CreateVersion7(), stelle, firma, wer, Text(nachricht), mitgeschickt,
            Bewerbungsstand.Submitted, jetzt, jetzt, null,
            kontakt ?? Bewerbungskontakt.Leer);

    /// <summary>Die Bewerbung, wie eine Zeile sie hält.</summary>
    public static Bewerbung Stelle_her(
        Guid id,
        Guid stelle,
        TenantId firma,
        SubjectId wer,
        string nachricht,
        Mitgeschicktes mitgeschickt,
        Bewerbungsstand stand,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        DateTimeOffset? beantwortetAm,
        Bewerbungskontakt? kontakt = null) =>
        new(id, stelle, firma, wer, nachricht, mitgeschickt, stand,
            angelegtAm, geaendertAm, beantwortetAm, kontakt ?? Bewerbungskontakt.Leer);

    /// <summary>Nach einem Rückzug erneut bewerben — eine neue Entscheidung.</summary>
    /// <remarks>
    /// Nach einer Ablehnung nicht: das wäre Nachfassen gegen ein „nein", das
    /// schon gefallen ist.
    /// </remarks>
    /// <exception cref="UebergangNichtErlaubt">Sie wurde nicht zurückgezogen.</exception>
    public void Schicke_erneut(
        string nachricht,
        Mitgeschicktes mitgeschickt,
        DateTimeOffset jetzt,
        Bewerbungskontakt? kontakt = null)
    {
        if (Stand is not Bewerbungsstand.Withdrawn)
        {
            throw new UebergangNichtErlaubt(Stand, Bewerbungsstand.Submitted);
        }

        Nachricht = Text(nachricht);
        Mitgeschickt = mitgeschickt;
        Kontakt = kontakt ?? Bewerbungskontakt.Leer;
        Stand = Bewerbungsstand.Submitted;
        BeantwortetAm = null;
        GeaendertAm = jetzt;
    }

    /// <summary>Zieht sie zurück.</summary>
    /// <remarks>
    /// Immer möglich, solange sie läuft — auch aus <c>Reviewing</c>: wer nicht
    /// mehr will, muss nicht warten, bis jemand anderes fertig ist.
    /// </remarks>
    /// <exception cref="NichtDeine">Sie gehört jemand anderem.</exception>
    /// <exception cref="UebergangNichtErlaubt">Sie läuft nicht mehr.</exception>
    public void Ziehe_zurueck(SubjectId durch, DateTimeOffset jetzt)
    {
        if (durch != Wer)
        {
            throw new NichtDeine();
        }

        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(Stand, Bewerbungsstand.Withdrawn);
        }

        Stand = Bewerbungsstand.Withdrawn;
        GeaendertAm = jetzt;
    }

    /// <summary>Das Unternehmen bewegt sie durch das Verfahren.</summary>
    /// <exception cref="UebergangNichtErlaubt">Von hier aus geht das nicht.</exception>
    public void Bewege(Bewerbungsstand ziel, DateTimeOffset jetzt)
    {
        if (ziel is not (Bewerbungsstand.Reviewing or Bewerbungsstand.Rejected
                         or Bewerbungsstand.Hired))
        {
            // Ein Unternehmen zieht keine Bewerbung zurück — das ist die
            // Handlung der Person, und sie hier zuzulassen hieße, es in ihrem
            // Namen tun zu können.
            throw new UebergangNichtErlaubt(Stand, ziel);
        }

        // Eine zurückgezogene Bewerbung ist keine mehr; eine abgelehnte oder
        // angenommene ist entschieden.
        if (!Laeuft)
        {
            throw new UebergangNichtErlaubt(Stand, ziel);
        }

        Stand = ziel;

        if (Endgueltig.Contains(ziel))
        {
            BeantwortetAm = jetzt;
        }

        GeaendertAm = jetzt;
    }

    private static string Text(string? nachricht)
    {
        var bereinigt = (nachricht ?? string.Empty).Trim();

        return bereinigt.Length > HoechstlaengeNachricht
            ? throw new NachrichtFehler(HoechstlaengeNachricht)
            : bereinigt;
    }
}

/// <summary>
/// Briefkopf zum Sendezeitpunkt. Gehört der Mappe, nicht dem Prompt (ADR-0038).
/// </summary>
public sealed record Bewerbungskontakt(
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
    public static Bewerbungskontakt Leer { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, "DE", string.Empty, string.Empty);
}

/// <summary>Findet und speichert Bewerbungen.</summary>
public interface IBewerbungsspeicher
{
    /// <summary>Eine Bewerbung, oder <c>null</c>.</summary>
    Task<Bewerbung?> HoleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Die Bewerbung dieser Person auf diese Stelle, falls es sie gibt.</summary>
    /// <remarks>
    /// Eine Person bewirbt sich einmal je Stelle. Ein zweites Mal ist entweder
    /// dieselbe Bewerbung erneut geschickt oder gar nicht erlaubt — nie eine
    /// zweite Zeile.
    /// </remarks>
    Task<Bewerbung?> HoleAsync(
        Guid stelle, SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Bewerbung bewerbung, CancellationToken cancellationToken = default);

    /// <summary>Die Bewerbungen einer Person, neueste zuerst.</summary>
    Task<IReadOnlyList<Bewerbung>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Die Bewerbungen auf eine Stelle dieses Unternehmens.</summary>
    Task<IReadOnlyList<Bewerbung>> FuerStelleAsync(
        Guid stelle, TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>Wie viele Bewerbungen dieses Unternehmen in welchem Stand hat.</summary>
    Task<IReadOnlyDictionary<Bewerbungsstand, int>> ZaehleAsync(
        TenantId firma, CancellationToken cancellationToken = default);
}

/// <summary>Die Worte, mit denen ein Stand auf der Leitung und in der Spalte steht.</summary>
/// <remarks>
/// An einer Stelle, weil es zwei Ränder gibt, die dasselbe Wort brauchen — die
/// Spalte <c>applications.status</c> und das Feld <c>status</c> im Vertrag. Zwei
/// Übersetzungen wären zwei Gelegenheiten, sich zu unterscheiden, und die
/// Oberfläche prüft die Worte einzeln ab.
/// </remarks>
public static class Bewerbungsstaende
{
    /// <summary>Das Wort zum Stand.</summary>
    public static string Wort(Bewerbungsstand stand) => stand switch
    {
        Bewerbungsstand.Submitted => "submitted",
        Bewerbungsstand.Reviewing => "reviewing",
        Bewerbungsstand.Rejected => "rejected",
        Bewerbungsstand.Withdrawn => "withdrawn",
        Bewerbungsstand.Hired => "hired",
        _ => throw new ArgumentOutOfRangeException(nameof(stand))
    };

    /// <summary>Der Stand zum Wort, oder <c>null</c>.</summary>
    public static Bewerbungsstand? Lies(string? wort) => wort switch
    {
        "submitted" => Bewerbungsstand.Submitted,
        "reviewing" => Bewerbungsstand.Reviewing,
        "rejected" => Bewerbungsstand.Rejected,
        "withdrawn" => Bewerbungsstand.Withdrawn,
        "hired" => Bewerbungsstand.Hired,
        _ => null
    };
}
