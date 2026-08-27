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

    private Stelle(
        Guid id,
        TenantId firma,
        string titel,
        string beschreibung,
        string ort,
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
        Remotegrad remote,
        Anstellungsart art,
        Faehigkeitenliste faehigkeiten,
        DateTimeOffset jetzt) =>
        new(
            Guid.CreateVersion7(), firma,
            Text("Der Titel", titel, pflicht: true, HoechstlaengeTitel),
            Text("Die Beschreibung", beschreibung, pflicht: true, HoechstlaengeBeschreibung),
            Text("Der Ort", ort, pflicht: false, HoechstlaengeOrt),
            remote, art, faehigkeiten, Stellenstand.Draft, jetzt, jetzt, null);

    /// <summary>Die Anzeige, wie eine Zeile sie hält.</summary>
    public static Stelle Stelle_her(
        Guid id,
        TenantId firma,
        string titel,
        string beschreibung,
        string ort,
        Remotegrad remote,
        Anstellungsart art,
        Faehigkeitenliste faehigkeiten,
        Stellenstand stand,
        DateTimeOffset angelegtAm,
        DateTimeOffset geaendertAm,
        DateTimeOffset? veroeffentlichtAm) =>
        new(id, firma, titel, beschreibung, ort, remote, art, faehigkeiten,
            stand, angelegtAm, geaendertAm, veroeffentlichtAm);

    /// <summary>Ändert den Inhalt.</summary>
    /// <exception cref="UebergangNichtErlaubt">Eine geschlossene Stelle ändert niemand mehr.</exception>
    public void Aendere(
        string titel,
        string beschreibung,
        string ort,
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
public sealed record Stellenseite(IReadOnlyList<Stelle> Eintraege, string? Weiter);

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
    Task<Stellenseite> SucheAsync(
        int anzahl,
        string? zeiger,
        IReadOnlyList<string>? faehigkeiten,
        string ort,
        Remotegrad? remote,
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
