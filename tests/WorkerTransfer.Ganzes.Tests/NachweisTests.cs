using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkerTransfer.Nachweis;
using WorkerTransfer.Nachweis.Pruefungen;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Infrastructure.Einwilligung;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Was dieses Feld trägt, ist der Gegenversuch — und er übersetzt.
/// </summary>
/// <remarks>
/// <para>Der Auftrag verlangt ihn wörtlich: <em>„ein Test, der ein Feld
/// <c>passung</c> in einen Vertrag einführt und beweist, dass
/// <c>wt.ki.keine-zahl</c> darauf rot wird. Eine Prüfung, die nie rot war, ist
/// keine."</em></para>
///
/// <para><strong>Ein echter Typ und kein Name in einer Zeichenkette.</strong>
/// Ein Gegenversuch, der die Prüfung mit <c>"Passung"</c> füttert, prüft die
/// Zeichenkettensuche; dieser hier prüft, dass die Absuche über eine Assembly
/// wirklich bei den Feldern ankommt. Er liegt in der Testassembly und nirgends
/// sonst — <c>Adr0022Tests</c> und <c>AuflagenTests</c> sehen github und scout
/// an, nicht diese Reihe.</para>
/// </remarks>
public sealed record KanarienvogelV1(string Name, int Passung);

/// <summary>Ein Ledger-Tor mit einem Zwischenspeicher davor — der Gegenversuch.</summary>
/// <remarks>
/// Er übersetzt, und das ist die halbe Miete: <c>else if (false)</c> wäre
/// unerreichbarer Code und damit ein <em>Bau</em>fehler, und ein Baufehler
/// liest sich in der Ausgabe wie ein bestandener Test.
/// </remarks>
public sealed class GemerktesTor(IEinwilligungstor dahinter) : IEinwilligungstor
{
    private bool? _gemerkt;

    /// <inheritdoc />
    public async Task<bool> DarfSehenAsync(
        Girder.Core.Identity.SubjectId wer,
        Girder.Core.Identity.TenantId firma,
        CancellationToken cancellationToken = default) =>
        _gemerkt ??= await dahinter.DarfSehenAsync(wer, firma, cancellationToken);
}

/// <summary>Eine Prüfung, die wirft — samt einem Wort, das nie herauskommen darf.</summary>
public sealed class Werferin : IPruefung
{
    /// <summary>Was in der Ausnahme steht. Ein Anbieter schriebe hier seinen Endpunkt hin.</summary>
    public const string Kanarienvogel = "sk-ant-geheim-0815";

    /// <inheritdoc />
    public string Id => "wt.ki.anbieter";

    /// <inheritdoc />
    public Bereich Bereich => Bereich.KI;

    /// <inheritdoc />
    public Task<Befund> LaufenAsync(CancellationToken ct = default) =>
        throw new InvalidOperationException(
            $"POST https://api.example.com/v1/messages fehlgeschlagen, key={Kanarienvogel}");
}

/// <summary>Die Prüfungen laufen, und jede kann rot werden.</summary>
/// <remarks>
/// <para><strong>Zu jeder Zusage ein Gegenversuch</strong>, und jeder ist
/// gefahren worden, bevor er hier steht. Eine Gegenprobe, die nicht fällt,
/// zeigt einen schwachen Test — nicht richtigen Code.</para>
///
/// <para><strong>Kein Test hier bestünde noch, wenn man den Rumpf der Prüfung
/// löschte.</strong> Das ist die Falle, die dieser Auftrag namentlich nennt: in
/// Noelia standen drei grüne Tests über auskommentierten Methoden. Jede
/// Behauptung unten nagelt deshalb einen <em>bestimmten</em> Stand und einen
/// Satzbestandteil fest, nicht bloß „es kam etwas zurück".</para>
/// </remarks>
public class NachweisTests
{
    // ------------------------------------------------------- wt.ki.keine-zahl

    /// <summary>Über Domäne und Verträgen von profile-service trägt kein Name eine Zahl.</summary>
    [Fact]
    public async Task Die_Zahlpruefung_findet_in_profile_service_nichts()
    {
        var befund = await new Zahlpruefung(
            [typeof(Profil).Assembly, typeof(Profile.Contracts.ProfilfundV1).Assembly])
            .LaufenAsync();

        befund.Stand.Should().Be(Stand.Erfuellt);
        befund.Id.Should().Be("wt.ki.keine-zahl");
        befund.Zusammenfassung.Should().Contain("keiner");
    }

    /// <summary>Und sie wird rot, sobald ein Vertrag ein Feld <c>Passung</c> trägt.</summary>
    /// <remarks>
    /// Der Gegenversuch aus Phase 5 des Auftrags. Gemessen: ohne
    /// <see cref="KanarienvogelV1"/> in dieser Assembly ist er grün, mit ihm rot.
    /// </remarks>
    [Fact]
    public async Task Die_Zahlpruefung_wird_rot_wenn_ein_Vertrag_eine_Passung_traegt()
    {
        var befund = await new Zahlpruefung([typeof(KanarienvogelV1).Assembly])
            .LaufenAsync();

        befund.Stand.Should().Be(Stand.Fehlt);
        befund.Zusammenfassung.Should().Contain("KanarienvogelV1.Passung");
        befund.Abhilfe.Should().Contain("ADR-0022");
    }

    /// <summary>Eine leere Fläche ist kein grüner Haken.</summary>
    /// <remarks>
    /// Sonst meldete die Prüfung grün über nichts — derselbe Fehler, den
    /// <c>scripts/test-dotnet.sh</c> mit seiner Zahl auf dem Schirm schließt.
    /// </remarks>
    [Fact]
    public async Task Die_Zahlpruefung_meldet_nicht_gruen_ueber_nichts()
    {
        var befund = await new Zahlpruefung([]).LaufenAsync();

        befund.Stand.Should().Be(Stand.Fehlt);
        befund.Zusammenfassung.Should().Contain("nichts angesehen");
    }

    // ------------------------------------------------------------- wt.ki.naht

    /// <summary>Die vereinbarte Feldmenge steht, und sie steht im Befund.</summary>
    [Fact]
    public async Task Die_Nahtpruefung_nennt_die_vier_Felder_und_den_Prompt()
    {
        var befund = await new Nahtpruefung(
            typeof(Entwurfslage),
            ["Ueberschrift", "Text", "Faehigkeiten", "Wunsch", "Prompt"],
            "für einen Profiltext").LaufenAsync();

        befund.Stand.Should().Be(Stand.Erfuellt);
        befund.Zusammenfassung.Should().Contain("Wunsch").And.Contain("Prompt");
    }

    /// <summary>Ein Feld weniger in der Erwartung heißt: da geht etwas Neues hinaus.</summary>
    /// <remarks>
    /// Der Gegenversuch von der anderen Seite: statt <see cref="Entwurfslage"/>
    /// zu erweitern — was den ganzen Baum änderte — wird die erwartete Menge
    /// verkleinert. Die Prüfung sieht denselben Unterschied, und sie sieht ihn
    /// an genau dem Typ, der in Betrieb ist.
    /// </remarks>
    [Fact]
    public async Task Die_Nahtpruefung_wird_rot_wenn_ein_Feld_dazukommt()
    {
        var befund = await new Nahtpruefung(
            typeof(Entwurfslage),
            ["Ueberschrift", "Text", "Faehigkeiten", "Prompt"],
            "für einen Profiltext").LaufenAsync();

        befund.Stand.Should().Be(Stand.Fehlt);
        befund.Zusammenfassung.Should().Contain("zusätzlich Wunsch");
    }

    // --------------------------------------------------------- wt.ki.anbieter

    /// <summary>Ohne Eintrag ist der Gegenstand nicht da — und das ist kein Haken.</summary>
    [Fact]
    public async Task Die_Anbieterpruefung_meldet_ohne_Eintrag_NichtAnwendbar()
    {
        var befund = await new Anbieterpruefung([]).LaufenAsync();

        befund.Stand.Should().Be(Stand.NichtAnwendbar);
        befund.Stand.Should().NotBe(
            Stand.Erfuellt,
            "ein grüner Haken an etwas, das gar nicht gilt, ist Rauschen in "
            + "genau dem Dokument, das Rauschen durchschneiden soll");
    }

    /// <summary>Sie zählt Menschen und nennt keinen.</summary>
    [Fact]
    public async Task Die_Anbieterpruefung_zaehlt_Menschen_statt_sie_zu_nennen()
    {
        var befund = await new Anbieterpruefung(
        [
            new Probequelle(
            [
                new Anbieterzeile("anthropic", "api.anthropic.com", Herkunft.Person, 2),
                new Anbieterzeile("anthropic", "api.anthropic.com", Herkunft.Person, 1),
                new Anbieterzeile(
                    "openai_compatible", "host.docker.internal", Herkunft.Person, 1)
            ])
        ]).LaufenAsync();

        befund.Stand.Should().Be(
            Stand.Hinweis, "ein Ziel liegt im öffentlichen Netz");
        befund.Zusammenfassung.Should().Contain("3 Mensch(en) auf api.anthropic.com");
        befund.Zusammenfassung.Should().Contain("eigenes Netz");
        befund.Abhilfe.Should().Contain("Art. 30");
    }

    /// <summary>Liegt alles im eigenen Netz, verlässt kein Wort das Haus.</summary>
    [Fact]
    public async Task Die_Anbieterpruefung_ist_gruen_wenn_alles_im_eigenen_Netz_liegt()
    {
        var befund = await new Anbieterpruefung(
        [
            new Probequelle(
                [new Anbieterzeile("openai_compatible", "ollama", Herkunft.Person, 4)])
        ]).LaufenAsync();

        befund.Stand.Should().Be(Stand.Erfuellt);
        befund.Zusammenfassung.Should().Contain("4 Mensch(en)");
    }

    /// <summary>Nicht eingerichtet heißt: keine Zeile, nicht eine leere.</summary>
    /// <remarks>
    /// Sonst stünde der Vorgabewert der Adresse in einem Dokument, als spräche
    /// jemand mit ihm.
    /// </remarks>
    [Fact]
    public async Task Ein_nicht_eingerichteter_Betreiberanbieter_liefert_keine_Zeile()
    {
        var zeilen = await new Betreiberquelle(
            "anthropic", "https://api.anthropic.com/v1/messages", eingerichtet: false)
            .LeseAsync();

        zeilen.Should().BeEmpty();
    }

    // --------------------------------------------------------- wt.ki.protokoll

    /// <summary>Ohne Naht gibt es nichts aufzuzeichnen.</summary>
    [Fact]
    public async Task Die_Aufzeichnungspruefung_meldet_ohne_Naht_NichtAnwendbar()
    {
        var befund = await new Aufzeichnungspruefung(nahtVorhanden: false, anbieterEingerichtet: false, null)
            .LaufenAsync();

        befund.Stand.Should().Be(Stand.NichtAnwendbar);
    }

    /// <summary>
    /// Mit Naht und ohne Aufzeichnung: ein Hinweis mit Datum, kein Mangel.
    /// </summary>
    /// <remarks>
    /// <strong>Das Datum ist der Punkt.</strong> Ein Dokument, das eine Pflicht
    /// von 2027 so darstellt, als binde sie heute, lädt den Leser ein, zu früh
    /// Geld auszugeben.
    /// </remarks>
    [Fact]
    public async Task Ohne_Aufzeichnung_steht_ein_Hinweis_mit_dem_Datum_der_Pflicht()
    {
        var befund = await new Aufzeichnungspruefung(nahtVorhanden: true, anbieterEingerichtet: true, null)
            .LaufenAsync();

        befund.Stand.Should().Be(Stand.Hinweis);
        befund.Stand.Should().NotBe(
            Stand.Fehlt,
            "dass nichts gespeichert wird, ist ADR-0024 und kein Mangel — ein "
            + "Tor, das darauf rot geht, schaltet der nächste Mensch ab");
        befund.Abhilfe.Should().Contain("02.12.2027");
    }

    /// <summary>
    /// Eine Naht ohne eingetragenen Anbieter ist nicht dasselbe wie keine Naht.
    /// </summary>
    /// <remarks>
    /// <strong>Am erzeugten Dokument gemessen, nicht vorher bedacht.</strong>
    /// scout-service meldete „Dieser Dienst fragt kein Modell", während
    /// <c>wt.ki.naht</c> zwei Zeilen darüber die Feldmenge seiner Ansprache
    /// auflistete — zwei Befunde auf einer Seite, die sich widersprechen. Wer
    /// das liest, glaubt einem von beiden und weiß nicht, welchem.
    /// </remarks>
    [Fact]
    public async Task Eine_Naht_ohne_Anbieter_sagt_das_und_leugnet_die_Naht_nicht()
    {
        var befund = await new Aufzeichnungspruefung(
            nahtVorhanden: true, anbieterEingerichtet: false, null).LaufenAsync();

        befund.Stand.Should().Be(Stand.NichtAnwendbar);
        befund.Zusammenfassung.Should().Contain("hat eine KI-Naht");
        befund.Zusammenfassung.Should().NotContain(
            "fragt kein Modell",
            "die Naht ist baulich da — nur ruft sie in dieser Instanz niemanden");
    }

    /// <summary>Und der Schalter, den niemand liest, wird benannt.</summary>
    [Fact]
    public async Task Ein_Schalter_ohne_Leser_steht_im_Befund()
    {
        var befund = await new Aufzeichnungspruefung(
            nahtVorhanden: true, anbieterEingerichtet: true, null,
            schalterVorhanden: true).LaufenAsync();

        befund.Zusammenfassung.Should().Contain("Anfragen protokollieren");
    }

    // ---------------------------------------------------- wt.einwilligung.wirkt

    /// <summary>Zwischen Frage und Ledger steht der Adapter selbst.</summary>
    [Fact]
    public async Task Die_Widerrufspruefung_ist_gruen_ohne_Zwischenstueck()
    {
        await using var anbieter = Profilbau().BuildServiceProvider();

        var befund = await new Widerrufspruefung<IEinwilligungstor>(
            anbieter, typeof(HttpEinwilligungstor)).LaufenAsync();

        befund.Stand.Should().Be(Stand.Erfuellt);
        befund.Zusammenfassung.Should().Contain("kein weiterer Typ");
    }

    /// <summary>Und rot, sobald jemand einen Zwischenspeicher davorsetzt.</summary>
    /// <remarks>
    /// Der Gegenversuch zu ADR-0013. Er übersetzt: <see cref="GemerktesTor"/>
    /// ist ein gültiger Dekorierer, genau so, wie ihn jemand in bester Absicht
    /// schriebe.
    /// </remarks>
    [Fact]
    public async Task Die_Widerrufspruefung_wird_rot_mit_einem_Zwischenspeicher()
    {
        var dienste = Profilbau();

        dienste.AddScoped<IEinwilligungstor>(anbieter =>
            new GemerktesTor(
                ActivatorUtilities.CreateInstance<HttpEinwilligungstor>(anbieter)));

        await using var anbieter = dienste.BuildServiceProvider();

        var befund = await new Widerrufspruefung<IEinwilligungstor>(
            anbieter, typeof(HttpEinwilligungstor)).LaufenAsync();

        befund.Stand.Should().Be(Stand.Fehlt);
        befund.Zusammenfassung.Should().Contain("GemerktesTor");
        befund.Abhilfe.Should().Contain("ADR-0013");
    }

    // --------------------------------------------------- wt.loeschung.nachweis

    /// <summary>Eine verschlossene Tür ist eine nicht eingelöste Löschzusage.</summary>
    /// <remarks>
    /// Der Befund, den heute nichts sieht: <c>LoeschempfaengerTests</c> prüft
    /// die <em>Liste</em> am EF-Modell und kann nicht wissen, ob das Geheimnis
    /// in dieser Umgebung gesetzt ist.
    /// </remarks>
    [Fact]
    public async Task Eine_geschlossene_Loeschtuer_wird_rot()
    {
        var befund = await Loeschpruefung.AlsEmpfaenger(
            "profile", tuerEingerichtet: false, pruefspurVorhanden: true)
            .LaufenAsync();

        befund.Stand.Should().Be(Stand.Fehlt);
        befund.Zusammenfassung.Should().Contain("Tür ist");
        befund.Abhilfe.Should().Contain("Erasure__Geheimnis");
    }

    /// <summary>Ein Dienst ohne Personenzeilen ist kein Empfänger — und kein Haken.</summary>
    [Fact]
    public async Task Ein_Dienst_ohne_Personenzeilen_meldet_NichtAnwendbar()
    {
        var befund = await Loeschpruefung.OhneZeilen("jobs").LaufenAsync();

        befund.Stand.Should().Be(Stand.NichtAnwendbar);
    }

    /// <summary>Offene Tür, aber keine Prüfspur: erreichbar, unbelegt.</summary>
    [Fact]
    public async Task Ohne_Pruefspur_bleibt_ein_Hinweis()
    {
        var befund = await Loeschpruefung.AlsEmpfaenger(
            "portfolio", tuerEingerichtet: true, pruefspurVorhanden: false)
            .LaufenAsync();

        befund.Stand.Should().Be(Stand.Hinweis);
        befund.Abhilfe.Should().Contain("Art. 5");
    }

    // ------------------------------------------------------- wt.grenze.ziele

    /// <summary>Nur Dienstnamen: nichts verlässt das Haus.</summary>
    [Fact]
    public async Task Die_Zielpruefung_ist_gruen_im_eigenen_Netz()
    {
        var befund = await new Zielpruefung(Konfiguration(new()
        {
            ["Consent:Adresse"] = "http://consent-service:8002",
            ["ConnectionStrings:profile"] = "Host=postgres;Database=profile"
        })).LaufenAsync();

        befund.Stand.Should().Be(Stand.Erfuellt);
        befund.Zusammenfassung.Should().Contain("consent-service");
        befund.Zusammenfassung.Should().NotContain(
            "postgres",
            "eine Verbindungszeichenfolge ist keine URL, und ein Datenbankhost "
            + "gehört in kein Empfängerverzeichnis nach Art. 30 DSGVO");
    }

    /// <summary>Ein öffentliches Ziel stellt eine Frage — und zwar in derselben Zeile.</summary>
    [Fact]
    public async Task Die_Zielpruefung_meldet_ein_oeffentliches_Ziel_als_Hinweis()
    {
        var befund = await new Zielpruefung(Konfiguration(new()
        {
            ["Consent:Adresse"] = "http://consent-service:8002",
            ["Draft:Adresse"] = "https://api.anthropic.com/v1/messages"
        })).LaufenAsync();

        befund.Stand.Should().Be(Stand.Hinweis);
        befund.Zusammenfassung.Should().Contain("api.anthropic.com");
        befund.Abhilfe.Should().Contain("Garantie");
    }

    // ----------------------------------------------------------- Der Lauf

    /// <summary>
    /// Eine Prüfung, die wirft, nimmt ihre Meldung nicht mit in den Bericht.
    /// </summary>
    /// <remarks>
    /// <strong>Der Kanarienvogel, und nicht das Hinsehen.</strong> Anbieter
    /// schreiben Endpunkte in ihre Ausnahmen, und mancher auch die Nutzlast.
    /// Übrig bleiben darf der Typname — eine Gestalt.
    /// </remarks>
    [Fact]
    public async Task Eine_werfende_Pruefung_traegt_ihre_Meldung_nicht_in_den_Bericht()
    {
        var lesung = await new Nachweislauf(
            "probe", [new Werferin()], TimeProvider.System).LeseAsync();

        var befund = lesung.Befunde.Single();

        befund.Stand.Should().Be(Stand.Fehlt, "ihr Gegenstand bleibt unbelegt");
        befund.Zusammenfassung.Should().Contain("InvalidOperationException");
        befund.Zusammenfassung.Should().NotContain(Werferin.Kanarienvogel);
        befund.Zusammenfassung.Should().NotContain("api.example.com");
        befund.Abhilfe.Should().NotContain(Werferin.Kanarienvogel);
    }

    /// <summary>Die Lesung steht sortiert — sonst ließe sich nichts nachrechnen.</summary>
    [Fact]
    public async Task Eine_Lesung_steht_nach_Kennung_sortiert()
    {
        var lesung = await new Nachweislauf(
            "probe",
            [
                new Zielpruefung(Konfiguration([])),
                new Anbieterpruefung([]),
                new Aufzeichnungspruefung(false, false, null)
            ],
            TimeProvider.System).LeseAsync();

        lesung.Befunde.Select(befund => befund.Id).Should().BeInAscendingOrder();
        lesung.Dienst.Should().Be("probe");
    }

    /// <summary>Nur <see cref="Stand.Fehlt"/> macht eine Lesung rot.</summary>
    [Fact]
    public async Task Ein_Hinweis_macht_die_Lesung_nicht_rot()
    {
        var lesung = await new Nachweislauf(
            "probe",
            [new Aufzeichnungspruefung(nahtVorhanden: true, anbieterEingerichtet: true, null)],
            TimeProvider.System).LeseAsync();

        lesung.Befunde.Single().Stand.Should().Be(Stand.Hinweis);
        lesung.IrgendetwasFehlt.Should().BeFalse();
    }

    // ------------------------------------------------------------- Werkzeug

    /// <summary>Eine Quelle, die vorgibt, was sie weiß.</summary>
    private sealed class Probequelle(IReadOnlyList<Anbieterzeile> zeilen) : IAnbieterquelle
    {
        public Task<IReadOnlyList<Anbieterzeile>> LeseAsync(CancellationToken ct = default) =>
            Task.FromResult(zeilen);
    }

    private static IConfiguration Konfiguration(Dictionary<string, string?> werte) =>
        new ConfigurationBuilder().AddInMemoryCollection(werte).Build();

    /// <summary>Der Verbundpunkt von profile-service, ohne Datenbank am Draht.</summary>
    /// <remarks>
    /// Aufgelöst wird hier nur das Einwilligungstor; der <c>DbContext</c> wird
    /// registriert und nie gebaut. Das ist der Grund, warum diese Reihe ohne
    /// Postgres läuft — und warum sie in <c>Ganzes.Tests</c> steht und nicht
    /// in der Profilreihe, die einen Behälter hochfährt.
    /// </remarks>
    private static ServiceCollection Profilbau()
    {
        var dienste = new ServiceCollection();

        dienste.AddHttpClient();
        // Das Tor fragt IM AUFTRAG DES AUFRUFERS und braucht dafuer
        // dessen Anfrage — ohne diese Zeile laesst es sich gar nicht
        // bauen, und die Pruefung meldete `Fehlt` ueber einen Fehler
        // dieser Vorrichtung statt ueber den Baum.
        dienste.AddHttpContextAccessor();
        dienste.AddOptions<Einwilligungseinstellungen>();
        dienste.AddScoped<IEinwilligungstor, HttpEinwilligungstor>();

        return dienste;
    }
}
