using Girder.Core.Identity;
using WorkerTransfer.Profile.Domain.Faehigkeiten;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>Das Profil einer Person — was sie selbst über sich einträgt.</summary>
/// <remarks>
/// Bewusst schmal: Überschrift, Freitext, Ort, Remote-Bereitschaft,
/// Fähigkeiten. Strukturierte Berufserfahrung ist der Lebenslauf, verifizierte
/// Nachweise wären ein eigener Dienst; beides würde hier nur doppelt gepflegt.
/// <para>
/// <b>Das Profil kennt keine Sichtbarkeit</b> (ADR-0020). Ob es jemand sehen
/// darf, beantwortet allein der Consent-Ledger. Ein Feld an dieser Stelle wäre
/// eine zweite Wahrheit — und die eine, die man vergisst mitzuändern.
/// </para>
/// <para>
/// Ein Profil je Person: die <see cref="SubjectId"/> <em>ist</em> der
/// Schlüssel. Es gibt keine eigene Profil-Id, weil es nichts gäbe, was sie
/// unterscheiden könnte.
/// </para>
/// </remarks>
public sealed class Profil
{
    /// <summary>Wie lang eine Überschrift sein darf.</summary>
    public const int HoechstlaengeUeberschrift = 120;

    /// <summary>Wie lang der Freitext sein darf.</summary>
    public const int HoechstlaengeText = 4000;

    /// <summary>Wie lang eine Ortsangabe sein darf.</summary>
    public const int HoechstlaengeOrt = 120;

    /// <summary>Wie lang der Wunsch an die Formulierungshilfe höchstens ist.</summary>
    /// <remarks>
    /// Deutlich kürzer als der Text (4000), weil er etwas anderes ist: eine
    /// Anweisung, kein Inhalt. Wer hier mehr braucht, schreibt keinen Wunsch
    /// mehr, sondern schickt Text an einen fremden Anbieter vorbei am Profil.
    /// </remarks>
    public const int HoechstlaengeWunsch = 500;

    private Profil(
        SubjectId wer,
        string ueberschrift,
        string text,
        string ort,
        bool remoteMoeglich,
        Faehigkeitenliste faehigkeiten,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm)
    {
        Wer = wer;
        Ueberschrift = ueberschrift;
        Text = text;
        Ort = ort;
        RemoteMoeglich = remoteMoeglich;
        Faehigkeiten = faehigkeiten;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
    }

    /// <summary>Wessen Profil. Zugleich der Primärschlüssel.</summary>
    public SubjectId Wer { get; }

    /// <summary>Die eine Zeile, die zuerst gelesen wird.</summary>
    public string Ueberschrift { get; private set; }

    /// <summary>Was die Person über sich schreibt.</summary>
    public string Text { get; private set; }

    /// <summary>Wo sie arbeitet. Freitext, weil eine Ortsliste immer jemanden vergisst.</summary>
    public string Ort { get; private set; }

    /// <summary>
    /// Ob sie aus der Ferne arbeiten würde.
    /// </summary>
    /// <remarks>
    /// <c>false</c> heißt „hat nicht ja gesagt“, nicht „lehnt ab“. Deshalb
    /// filtert die Suche nur in eine Richtung.
    /// </remarks>
    public bool RemoteMoeglich { get; private set; }

    /// <summary>Was sie kann — in ihrer Reihenfolge, ohne Bewertung.</summary>
    public Faehigkeitenliste Faehigkeiten { get; private set; }

    /// <summary>Wann das Profil entstand.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann es zuletzt geändert wurde. Zugleich der halbe Seitenzeiger.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary>Legt ein Profil an.</summary>
    /// <exception cref="UeberschriftFehler">Die Überschrift fehlt oder ist zu lang.</exception>
    /// <exception cref="TextFehler">Der Text ist zu lang.</exception>
    /// <exception cref="OrtFehler">Der Ort ist zu lang.</exception>
    public static Profil Lege_an(
        SubjectId wer,
        string ueberschrift,
        string text,
        string ort,
        bool remoteMoeglich,
        Faehigkeitenliste faehigkeiten,
        DateTimeOffset jetzt)
    {
        var (geprueft, gepruefterText, geprueterOrt) = Pruefe(ueberschrift, text, ort);

        return new Profil(
            wer, geprueft, gepruefterText, geprueterOrt, remoteMoeglich,
            faehigkeiten ?? Faehigkeitenliste.Leer, jetzt, jetzt);
    }

    /// <summary>Baut ein gespeichertes Profil wieder auf.</summary>
    /// <remarks>
    /// Der einzige Eingang für eine bestehende Zeile, und er prüft <em>nicht</em>
    /// erneut. Eine gespeicherte Zeile war bei ihrer Entstehung gültig; sie
    /// beim Lesen abzulehnen hieße, jemandem sein Profil zu entziehen, weil sich
    /// eine Obergrenze geändert hat.
    /// </remarks>
    public static Profil Stelle_her(
        SubjectId wer,
        string ueberschrift,
        string text,
        string ort,
        bool remoteMoeglich,
        Faehigkeitenliste faehigkeiten,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm) =>
        new(wer, ueberschrift, text, ort, remoteMoeglich,
            faehigkeiten ?? Faehigkeitenliste.Leer, angelegtAm, geaendertAm);

    /// <summary>Ändert alle Felder auf einmal.</summary>
    /// <remarks>
    /// Erst vollständig prüfen, dann schreiben: ein abgelehntes Formular darf
    /// kein halb geändertes Aggregat hinterlassen.
    /// </remarks>
    /// <exception cref="UeberschriftFehler">Die Überschrift fehlt oder ist zu lang.</exception>
    /// <exception cref="TextFehler">Der Text ist zu lang.</exception>
    /// <exception cref="OrtFehler">Der Ort ist zu lang.</exception>
    public void Aendere(
        string ueberschrift,
        string text,
        string ort,
        bool remoteMoeglich,
        Faehigkeitenliste faehigkeiten,
        DateTimeOffset jetzt)
    {
        var (geprueft, gepruefterText, geprueterOrt) = Pruefe(ueberschrift, text, ort);

        Ueberschrift = geprueft;
        Text = gepruefterText;
        Ort = geprueterOrt;
        RemoteMoeglich = remoteMoeglich;
        Faehigkeiten = faehigkeiten ?? Faehigkeitenliste.Leer;
        GeaendertAm = jetzt;
    }

    /// <summary>
    /// Prüft und putzt die drei Textfelder gemeinsam.
    /// </summary>
    /// <remarks>
    /// Gemeinsam, damit Anlegen und Ändern nicht auseinanderlaufen können.
    /// </remarks>
    private static (string Ueberschrift, string Text, string Ort) Pruefe(
        string ueberschrift, string text, string ort)
    {
        var geputzt = (ueberschrift ?? string.Empty).Trim();

        if (geputzt.Length == 0)
        {
            throw new UeberschriftFehler("darf nicht leer sein");
        }

        if (geputzt.Length > HoechstlaengeUeberschrift)
        {
            throw new UeberschriftFehler(
                $"darf höchstens {HoechstlaengeUeberschrift} Zeichen haben");
        }

        var geputzterText = (text ?? string.Empty).Trim();

        if (geputzterText.Length > HoechstlaengeText)
        {
            throw new TextFehler();
        }

        var geputzterOrt = (ort ?? string.Empty).Trim();

        return geputzterOrt.Length > HoechstlaengeOrt
            ? throw new OrtFehler()
            : (geputzt, geputzterText, geputzterOrt);
    }
}
