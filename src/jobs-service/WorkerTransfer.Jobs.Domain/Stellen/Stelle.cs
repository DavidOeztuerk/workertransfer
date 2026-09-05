using Girder.Core.Identity;

namespace WorkerTransfer.Jobs.Domain.Stellen;

/// <summary>Wo eine Stelle steht.</summary>
public enum Stellenstand
{
    /// <summary>Geschrieben, nicht veröffentlicht.</summary>
    Draft,

    /// <summary>Sichtbar.</summary>
    Published,

    /// <summary>Zurückgezogen oder besetzt.</summary>
    Closed
}

/// <summary>Wie viel Remote möglich ist.</summary>
/// <remarks>
/// Eine Aufzählung, kein Wahrheitswert. „Remote möglich?" ist die Frage, die
/// alle stellen, und „ja/nein" beantwortet sie falsch: hybrid ist der häufigste
/// Fall und keine Zwischenstufe von wahr.
/// </remarks>
public enum Remotegrad
{
    /// <summary>Vor Ort.</summary>
    None,

    /// <summary>Teilweise.</summary>
    Hybrid,

    /// <summary>Ganz.</summary>
    Full
}

/// <summary>Welche Art von Anstellung.</summary>
public enum Anstellungsart
{
    FullTime,
    PartTime,
    Contract,
    Internship
}

/// <summary>Eine Stellenanzeige.</summary>
/// <remarks>
/// Sie gehört einem Unternehmen, nicht einem Menschen. Deshalb ist dieser
/// Dienst <b>kein Empfänger der Löschkaskade</b> (ADR-0027 §2): er hält nichts
/// über eine natürliche Person, und ein Löschbefehl an ihn wäre ein Endpunkt,
/// der „erledigt" sagt, ohne je etwas zu tun.
/// <para>
/// Es gibt hier keinen Punktwert, keine Rangfolge und keine Passung. Wie gut
/// jemand zu einer Stelle passt, wird im Browser gerechnet, der Person gezeigt
/// und nie dem Unternehmen — und es ordnet <em>Stellen</em>, nicht Menschen
/// (ADR-0022).
/// </para>
/// </remarks>
public sealed class Stelle
{
    /// <summary>Wie lang ein Titel sein darf.</summary>
    public const int HoechstlaengeTitel = 160;

    /// <summary>Wie lang eine Beschreibung sein darf.</summary>
    public const int HoechstlaengeBeschreibung = 20000;

    /// <summary>Wie lang eine Ortsangabe sein darf.</summary>
    public const int HoechstlaengeOrt = 160;

    /// <summary>Wie lang eine Postleitzahl sein darf.</summary>
    /// <remarks>
    /// Zehn, nicht fünf: Deutschland hat fünf Stellen, Österreich und die
    /// Schweiz vier, und „D-10115" schreiben genug Leute, dass ein Feld, das
    /// es abweist, mehr Ärger macht als es Ordnung schafft. Gelesen werden
    /// ohnehin nur die Ziffern (<c>Ortskunde</c>).
    /// </remarks>
    public const int HoechstlaengePlz = 10;

    /// <summary>Wie lang der Wunsch an die Formulierungshilfe höchstens ist.</summary>
    /// <remarks>
    /// Dieselbe Zahl wie im profile-service, und aus demselben Grund: der Wunsch
    /// ist eine Anweisung, kein Inhalt. Die Beschreibung darf 20000 Zeichen
    /// haben — sie ist die Anzeige. Der Wunsch ist es nicht.
    /// </remarks>
    public const int HoechstlaengeWunsch = 500;

    private Stelle(
        Guid id,
        TenantId firma,
        string titel,
        string beschreibung,
        string ort,
        string postleitzahl,
        Remotegrad remote,
        Anstellungsart art,
        Faehigkeitenliste faehigkeiten,
        Stellenstand stand,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        DateTimeOffset? veroeffentlichtAm)
    {
        Id = id;
        Firma = firma;
        Titel = titel;
        Beschreibung = beschreibung;
        Ort = ort;
        Postleitzahl = postleitzahl;
        Remote = remote;
        Art = art;
        Faehigkeiten = faehigkeiten;
        Stand = stand;
        AngelegtAm = angelegtAm;
        GeaendertAm = geaendertAm;
        VeroeffentlichtAm = veroeffentlichtAm;
    }

    /// <summary>Welche Anzeige.</summary>
    public Guid Id { get; }

    /// <summary>Wem sie gehört.</summary>
    public TenantId Firma { get; }

    /// <summary>Worum es geht.</summary>
    public string Titel { get; private set; }

    /// <summary>Was das Unternehmen selbst geschrieben hat.</summary>
    public string Beschreibung { get; private set; }

    /// <summary>Wo.</summary>
    public string Ort { get; private set; }

    /// <summary>Die Postleitzahl. Leer, wenn keine angegeben wurde.</summary>
    /// <remarks>
    /// <strong>Ein eigenes Feld und nicht Teil von <see cref="Ort"/>.</strong>
    /// Der Ortsname ist Freitext und darf mehrdeutig sein — „Neustadt" gibt es
    /// zwanzigmal. Die Postleitzahl ist es nicht, und nur sie macht die
    /// Umkreissuche über einem kleinen Ort verlässlich. Ohne sie bleibt die
    /// Anzeige auffindbar, fällt aber aus einer Umkreissuche heraus, wenn ihr
    /// Ortsname nicht eindeutig ist — und das wird der suchenden Person gesagt
    /// (ADR-0032), nicht verschwiegen.
    /// </remarks>
    public string Postleitzahl { get; private set; }

    /// <summary>Wie viel Remote.</summary>
    public Remotegrad Remote { get; private set; }

    /// <summary>Welche Anstellung.</summary>
    public Anstellungsart Art { get; private set; }

    /// <summary>Was verlangt wird.</summary>
    public Faehigkeitenliste Faehigkeiten { get; private set; }

    /// <summary>Wo sie steht.</summary>
    public Stellenstand Stand { get; private set; }

    /// <summary>Wann sie geschrieben wurde.</summary>
    public DateTimeOffset AngelegtAm { get; }

    /// <summary>Wann sie zuletzt geändert wurde.</summary>
    public DateTimeOffset GeaendertAm { get; private set; }

    /// <summary><c>null</c>, solange sie nie veröffentlicht war.</summary>
    public DateTimeOffset? VeroeffentlichtAm { get; private set; }

    /// <summary>Schreibt eine neue Anzeige. Sie beginnt als Entwurf.</summary>
    /// <remarks>
    /// Nie sofort sichtbar. Wer eine Anzeige schreibt, soll sie lesen können,
    /// bevor Bewerbungen darauf eingehen.
    /// </remarks>
    public static Stelle Lege_an(
        TenantId firma,
        string titel,
        string beschreibung,
        string ort,
        string postleitzahl,
        Remotegrad remote,
        Anstellungsart art,
        Faehigkeitenliste faehigkeiten,
        DateTimeOffset jetzt) =>
        new(
            Guid.CreateVersion7(), firma,
            Text("Der Titel", titel, pflicht: true, HoechstlaengeTitel),
            Text("Die Beschreibung", beschreibung, pflicht: true, HoechstlaengeBeschreibung),
            Text("Der Ort", ort, pflicht: false, HoechstlaengeOrt),
            Text("Die Postleitzahl", postleitzahl, pflicht: false, HoechstlaengePlz),
            remote, art, faehigkeiten, Stellenstand.Draft, jetzt, jetzt, null);

    /// <summary>Die Anzeige, wie eine Zeile sie hält.</summary>
    public static Stelle Stelle_her(
        Guid id,
        TenantId firma,
        string titel,
        string beschreibung,
        string ort,
        string postleitzahl,
        Remotegrad remote,
        Anstellungsart art,
        Faehigkeitenliste faehigkeiten,
        Stellenstand stand,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        DateTimeOffset? veroeffentlichtAm) =>
        new(id, firma, titel, beschreibung, ort, postleitzahl, remote, art, faehigkeiten,
            stand, angelegtAm, geaendertAm, veroeffentlichtAm);

    /// <summary>Ändert den Inhalt.</summary>
    /// <exception cref="UebergangNichtErlaubt">Eine geschlossene Stelle ändert niemand mehr.</exception>
    public void Aendere(
        string titel,
        string beschreibung,
        string ort,
        string postleitzahl,
        Remotegrad remote,
        Anstellungsart art,
        Faehigkeitenliste faehigkeiten,
        DateTimeOffset jetzt)
    {
        // Eine geschlossene Anzeige zu ändern hieße, den Text zu verändern, auf
        // den sich Menschen beworben haben.
        if (Stand is Stellenstand.Closed)
        {
            throw new UebergangNichtErlaubt(Stand, Stellenstand.Draft);
        }

        Titel = Text("Der Titel", titel, pflicht: true, HoechstlaengeTitel);
        Beschreibung = Text(
            "Die Beschreibung", beschreibung, pflicht: true, HoechstlaengeBeschreibung);
        Ort = Text("Der Ort", ort, pflicht: false, HoechstlaengeOrt);
        Postleitzahl = Text(
            "Die Postleitzahl", postleitzahl, pflicht: false, HoechstlaengePlz);
        Remote = remote;
        Art = art;
        Faehigkeiten = faehigkeiten;
        GeaendertAm = jetzt;
    }

    /// <summary>Macht sie sichtbar.</summary>
    /// <remarks>
    /// Auch eine geschlossene darf wieder heraus: eine Stelle, die neu besetzt
    /// werden muss, ist derselbe Text.
    /// </remarks>
    public void Veroeffentliche(DateTimeOffset jetzt)
    {
        if (Stand is Stellenstand.Published)
        {
            return;
        }

        Stand = Stellenstand.Published;
        VeroeffentlichtAm ??= jetzt;
        GeaendertAm = jetzt;
    }

    /// <summary>Zieht sie zurück.</summary>
    /// <remarks>
    /// Ein Entwurf kann auch geschlossen werden — er ist dann verworfen, und
    /// das ist ein legitimer Ausgang.
    /// </remarks>
    public void Schliesse(DateTimeOffset jetzt)
    {
        Stand = Stellenstand.Closed;
        GeaendertAm = jetzt;
    }

    private static string Text(string feld, string? wert, bool pflicht, int grenze)
    {
        var bereinigt = (wert ?? string.Empty).Trim();

        if (pflicht && bereinigt.Length == 0)
        {
            throw new TextFehler(feld, "darf nicht leer sein");
        }

        return bereinigt.Length > grenze
            ? throw new TextFehler(feld, $"ist länger als {grenze} Zeichen")
            : bereinigt;
    }
}

/// <summary>Eine Seite Stellen und wo es weitergeht.</summary>
/// <summary>Eine Seite Stellen und wie viele es insgesamt gibt.</summary>
/// <remarks>
/// <strong>Gesamtzahl statt Zeiger</strong>, seit die Liste blätterbar ist: wer
/// „Seite 3 von 9" sehen und dorthin springen will, braucht die Zahl. Ein Zeiger
/// kann nur vorwärts, und die Frage „wie viele gibt es überhaupt" beantwortet er
/// nie.
/// <para>
/// Gezählt werden ANZEIGEN. Auf einer Kandidatenliste wäre dieselbe Zahl eine
/// Aussage über Menschen und damit ADR-0026 zuwider; deshalb steht sie hier und
/// nicht in einem gemeinsamen Listentyp.
/// </para>
/// </remarks>
/// <param name="Eintraege">Die Stellen dieser Seite.</param>
/// <param name="Gesamt">Wie viele Stellen der Filter insgesamt trifft.</param>
/// <param name="OhneOrt">
/// Wie viele Anzeigen eine Umkreissuche NICHT beurteilen konnte, weil ihre
/// Ortsangabe unbekannt ist. Ohne Umkreissuche immer null.
/// <para>
/// Diese Zahl muss die Oberfläche nennen. Sie stillschweigend wegzulassen wäre
/// dieselbe Lüge durch Auslassen, die ADR-0022 §3 verbietet: wer nach „25 km um
/// mich" sucht, hält das Ergebnis sonst für vollständig, obwohl der Filter über
/// einen Teil der Anzeigen gar nichts sagen konnte.
/// </para>
/// </param>
public sealed record Stellenseite(IReadOnlyList<Stelle> Eintraege, int Gesamt, int OhneOrt = 0);

/// <summary>Findet und speichert Anzeigen.</summary>
public interface IStellenspeicher
{
    /// <summary>Eine Anzeige, oder <c>null</c>.</summary>
    Task<Stelle?> HoleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Legt an oder schreibt zurück.</summary>
    Task SichereAsync(Stelle stelle, CancellationToken cancellationToken = default);

    /// <summary>Die Anzeigen eines Unternehmens, alle Stände.</summary>
    Task<IReadOnlyList<Stelle>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default);

    /// <summary>
    /// Die öffentliche Liste — nur veröffentlichte.
    /// </summary>
    /// <remarks>
    /// Der Stand wird hier gefiltert und nicht vom Aufrufer: ein Parameter
    /// <c>status=draft</c> wäre ein Weg, fremde Entwürfe zu lesen.
    /// </remarks>
    /// <param name="seite">
    /// Die Seite, 1-basiert. Menschen zählen ab eins, und diese Zahl steht so
    /// in der Adresszeile.
    /// </param>
    /// <param name="suchbegriff">
    /// Freitext über Titel und Beschreibung. Leer heisst „alles".
    /// </param>
    /// <param name="firma">
    /// Nur die Anzeigen eines Unternehmens — für die Karriereseite. <c>null</c>
    /// heisst „alle". Ohne diesen Filter zeigte <c>/careers/&lt;kürzel&gt;</c>
    /// die Anzeigen ALLER Unternehmen unter dem Namen eines einzigen; der
    /// Parameter wurde von der Oberfläche seit jeher geschickt und vom Dienst
    /// nie gelesen.
    /// </param>
    /// <param name="beschaeftigung">
    /// Vollzeit, Teilzeit und so weiter. Leer heisst „alles".
    /// </param>
    /// <param name="umkreis">
    /// „Höchstens N Kilometer von hier." <c>null</c> heisst „egal wo".
    /// <para>
    /// Voll remote ausgeschriebene Stellen sind IMMER dabei, unabhängig vom
    /// Radius: von wo aus sie erreichbar sind, ist bei ihnen keine Frage der
    /// Entfernung. Sie wegen eines Ortsfilters auszublenden hiesse, genau die
    /// Anzeigen zu verstecken, die für jemanden ausserhalb der Ballungsräume
    /// die interessantesten sind.
    /// </para>
    /// </param>
    Task<Stellenseite> SucheAsync(
        int seite,
        int anzahl,
        IReadOnlyList<string>? faehigkeiten,
        string ort,
        Remotegrad? remote,
        string suchbegriff = "",
        Guid? firma = null,
        string beschaeftigung = "",
        Umkreis? umkreis = null,
        CancellationToken cancellationToken = default);

    /// <summary>Zieht alle Anzeigen eines Unternehmens zurück.</summary>
    /// <returns>Wie viele zurückgezogen wurden.</returns>
    /// <remarks>
    /// Der Sonderfall aus ADR-0027 §7: die letzte Person mit
    /// <c>role='admin'</c> hat ihr Konto gelöscht. Das Unternehmen wird
    /// stillgelegt, nicht gelöscht — es ist keine natürliche Person, und seine
    /// Anzeigen gehören ihm auch dann noch. Aber eine unbeaufsichtigte
    /// Stellenanzeige ist schlechter als keine: Bewerbungen liefen an niemanden.
    /// </remarks>
    Task<int> ZieheZurueckAsync(
        TenantId firma, DateTimeOffset jetzt, CancellationToken cancellationToken = default);
}
