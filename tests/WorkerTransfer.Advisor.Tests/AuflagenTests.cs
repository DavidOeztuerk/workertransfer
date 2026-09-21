using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Application.Gespraeche;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Application.Mandates;
using WorkerTransfer.Advisor.Contracts;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandates;
using WorkerTransfer.Advisor.Infrastructure.Persistence;

namespace WorkerTransfer.Advisor.Tests;

/// <summary>
/// Die Sätze, um derentwillen dieser Dienst gebaut werden durfte — jeder als
/// Test.
/// </summary>
/// <remarks>
/// <para>Sie stehen in <em>einer</em> Datei, und das ist Absicht: wer einen
/// davon rot macht, hat nicht einen Test gebrochen, sondern die Begründung des
/// ganzen Dienstes.</para>
///
/// <para>Geprüft wird, wo möglich, an <em>Typen</em> und am <em>EF-Modell</em>
/// statt am Verhalten: eine Verhaltensprobe beantwortet „tut es das heute
/// nicht?", eine Typprobe verlangt, dass wer es ändert, die Menge hier
/// hinschreibt. Dieselbe Bewegung, aber eine, die auffällt.</para>
/// </remarks>
public class AuflagenTests
{
    private static readonly Guid Firma = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Anna = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---------------------------------------------------------------------
    // AUFLAGE 1 — DAS MANDAT IST EINE SICHT, KEIN ZWEITER SPEICHER
    // ---------------------------------------------------------------------

    /// <summary>Worte, die eine zweite Wahrheit über Sichtbarkeit ankündigen.</summary>
    /// <remarks>
    /// Die Liste aus ADR-0037 Entscheidung 1, plus die deutschen Zwillinge.
    /// Sichtbarkeit lebt im Ledger; eine Spalte hier wäre die zweite Wahrheit,
    /// und sie liefe beim ersten Revocation auseinander.
    /// </remarks>
    private static readonly string[] Verboten =
    [
        "sichtbar", "visible", "public", "freigabe", "released", "stage", "stufe"
    ];

    /// <summary>None Spalte des EF-Modells trägt ein solches Wort.</summary>
    /// <remarks>
    /// <para>Am <em>Modell</em> und nicht am Quelltext: eine Spalte kann aus
    /// einer Basisklasse oder einer Konvention kommen, und ein regulärer
    /// Ausdruck übersähe sie. Was hier gelesen wird, ist genau das, was in der
    /// Datenbank entsteht.</para>
    ///
    /// <para><c>stage</c> und <c>stufe</c> stehen mit auf der Liste, und das ist
    /// die schärfere Hälfte: eine Stufenspalte wäre die Sichtbarkeit als zweite
    /// Tür neben dem Ledger, und ein Revocation müsste dann an zwei Jobs
    /// wirken — also irgendwann an einer nicht.</para>
    /// </remarks>
    [Fact]
    public void Keine_Spalte_haelt_eine_Sichtbarkeit()
    {
        var columns = Spalten();

        columns.Should().NotBeEmpty("sonst prüft dieser Test nichts");

        foreach (var column in columns)
        {
            foreach (var word in Verboten)
            {
                column.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{column}' trägt '{word}' — Sichtbarkeit lebt im Ledger "
                        + "(ADR-0020, ADR-0037 Entscheidung 1)");
            }
        }
    }

    /// <summary>Die Erkennung selbst stimmt — sonst wäre der Test grün, weil er nichts findet.</summary>
    [Theory]
    [InlineData("entry_month", false)]
    [InlineData("excluded_domains", false)]
    [InlineData("note", false)]
    [InlineData("is_public", true)]
    [InlineData("stage", true)]
    [InlineData("sichtbar_fuer", true)]
    public void Die_Erkennung_trennt_richtig(string column, bool erwartet) =>
        Verboten.Any(word => column.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(erwartet);

    /// <summary>Das Mandate trägt genau diese Werte — vier, und einen Zeitstempel.</summary>
    /// <remarks>
    /// Die geschlossene Feldmenge, nicht eine Verbotsliste: wer ein Feld
    /// ergänzt, schreibt es hier hin — und beantwortet dabei, ob es wirklich
    /// nirgends sonst steht. Nur das darf dazu.
    /// </remarks>
    [Fact]
    public void Ein_Mandat_traegt_genau_diese_Felder()
    {
        Felder(typeof(Mandate)).Should().BeEquivalentTo(
            "Wer", "Eintrittstermin", "GehaltMin", "GehaltMax", "PensumProzent",
            "AusgeschlosseneUnternehmen", "GeaendertAm", "Leer");

        Felder(typeof(MandateV1)).Should().BeEquivalentTo(
            "EntryMonth", "SalaryMin", "SalaryMax", "WorkloadPercent",
            "ExcludedDomains", "UpdatedAt");
    }

    /// <summary>Und das Gespräch trägt keine Stufe.</summary>
    /// <remarks>
    /// Der Zwilling zur Spaltenprüfung, eine Ebene höher: nicht einmal im
    /// Aggregat gibt es eine Stufe, also kann sie auch nicht versehentlich
    /// gespeichert werden. Was sie ist, weiss allein der Ledger.
    /// </remarks>
    [Fact]
    public void Ein_Gespraech_traegt_genau_diese_Felder() =>
        Felder(typeof(Conversation)).Should().BeEquivalentTo(
            "Id", "Wer", "Firma", "State", "Anlass", "EroeffnetAm", "GeaendertAm", "Laeuft");

    // ---------------------------------------------------------------------
    // AUFLAGE 2 — DIE STUFE KOMMT AUS DEM LEDGER, BEI JEDEM LESEN
    // ---------------------------------------------------------------------

    /// <summary>Jedes Lesen fragt den Ledger erneut.</summary>
    /// <remarks>
    /// ADR-0013: ein Revocation muss beim nächsten Lesen wirken. Ein
    /// Zwischenspeicher wäre hier kein Leistungsdetail, sondern ein Regelbruch
    /// — und er fiele erst auf, wenn jemand zurückgezogen hat und trotzdem
    /// gesehen wird.
    /// </remarks>
    [Fact]
    public async Task Jedes_Lesen_fragt_den_Ledger_erneut()
    {
        var (conversations, mandate, tor, personen) = Aufbau();
        await Eroeffne(conversations, tor);

        var handler = new CompanyConversationsHandler(conversations, mandate, tor, personen);

        var vorher = tor.Fragen;

        await handler.Handle(new CompanyConversationsQuery(new TenantId(Firma)), default);
        await handler.Handle(new CompanyConversationsQuery(new TenantId(Firma)), default);

        (tor.Fragen - vorher).Should().Be(2);
    }

    /// <summary>Eine Release schreibt Ledger-Ereignisse — und zwar kumulativ.</summary>
    /// <remarks>
    /// Wer Stufe 3 freigibt, erteilt auch 1 und 2. Sonst stünde die neue
    /// Fähigkeit da, während die darunter fehlt, und ein Lesen ergäbe trotzdem
    /// <c>None</c>: eine Stufe ist ein State und kein Sprung.
    /// </remarks>
    [Fact]
    public async Task Eine_Freigabe_erteilt_alles_bis_zu_ihrer_Stufe()
    {
        var (conversations, _, tor, _) = Aufbau();
        var conversation = await Eroeffne(conversations, tor);

        tor.Selbst = Anna;

        await new StufeFreigebenHandler(conversations, tor).Handle(
            new StufeFreigebenBefehl(conversation.Id, new SubjectId(Anna), Stufe.Person), default);

        var tenant = new TenantId(Firma);

        tor.Erteilungen.Should().BeEquivalentTo(
            [
                StageCapabilities.Profile(tenant),
                StageCapabilities.Markt(tenant),
                StageCapabilities.Resume(tenant),
                StageCapabilities.Unterlagen(tenant),
                StageCapabilities.Klarname(tenant)
            ],
            "eine Stufe ist ein Stand: wer 3 freigibt, erteilt auch 1 und 2");
    }

    /// <summary>
    /// Und sie erteilt <c>profile.visibility:public</c> NICHT.
    /// </summary>
    /// <remarks>
    /// Die Gegenprobe zur vorigen Zeile, und sie ist die wichtigere: eine
    /// Stufenfreigabe gilt <em>einem</em> Unternehmen. Aus ihr eine
    /// plattformweite Sichtbarkeit zu machen wäre genau der Schalter, dessen
    /// Folgen niemand überblickt — darunter der eigene Arbeitgeber, der auf
    /// derselben Plattform ist.
    /// </remarks>
    [Fact]
    public async Task Eine_Freigabe_macht_niemanden_fuer_alle_sichtbar()
    {
        var (conversations, _, tor, _) = Aufbau();
        var conversation = await Eroeffne(conversations, tor);

        tor.Selbst = Anna;

        await new StufeFreigebenHandler(conversations, tor).Handle(
            new StufeFreigebenBefehl(conversation.Id, new SubjectId(Anna), Stufe.Person), default);

        tor.Erteilungen.Should().NotContain(StageCapabilities.ProfilOeffentlich);
        tor.Erteilungen.Should().NotContain("github.visibility:public");
    }

    /// <summary>Ein Revocation nimmt alles darüber mit — und wirkt sofort.</summary>
    [Fact]
    public async Task Ein_Widerruf_nimmt_alles_darueber_mit()
    {
        var (conversations, mandate, tor, personen) = Aufbau();
        var conversation = await Eroeffne(conversations, tor);

        tor.Selbst = Anna;
        tor.Job(Anna, Firma, Stufe.Person);

        var steht = await new RevokeStageHandler(conversations, tor).Handle(
            new RevokeStageCommand(conversation.Id, new SubjectId(Anna), Stufe.Profile), default);

        steht.Should().Be(Stufe.None, "ein Widerruf wirkt bei der nächsten Anfrage");

        tor.Widerrufe.Should().Contain(StageCapabilities.Klarname(new TenantId(Firma)));
        tor.Gruende.Should().OnlyContain(reason => reason == RevokeStageHandler.Grund);

        // Und das Conversation faellt damit aus der Liste des Unternehmens: eine
        // leere Zeile darin waere der „gesperrt"-Hinweis in Listenform.
        var ihre = await new CompanyConversationsHandler(conversations, mandate, tor, personen)
            .Handle(new CompanyConversationsQuery(new TenantId(Firma)), default);

        ihre.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 3 — WAS NICHT FREIGEGEBEN IST, EXISTIERT NICHT
    // ---------------------------------------------------------------------

    /// <summary>Auf Stufe 1 gibt es kein Feld für ein Gehalt und keines für einen Namen.</summary>
    /// <remarks>
    /// <para><strong>Geprüft am JSON und nicht am Objekt</strong>, denn genau
    /// das ist die Zusage: nicht <c>"salary_min": null</c>, sondern gar kein
    /// Schlüssel. Ein genulltes Feld sagt „es gibt hier ein Gehalt, du siehst es
    /// nur nicht" — und damit, dass es etwas zu sehen gäbe.</para>
    /// </remarks>
    [Fact]
    public void Auf_Stufe_eins_fehlen_die_Felder_der_hoeheren_Stufen()
    {
        var json = Draht(Stufe.Profile);

        json.Should().Contain("\"entry_month\"");
        json.Should().Contain("\"workload_percent\"");

        json.Should().NotContain("salary_min");
        json.Should().NotContain("salary_max");
        json.Should().NotContain("\"name\"");
        json.Should().NotContain("\"email\"");
    }

    /// <summary>Auf Stufe 2 kommt die Spanne dazu, der Name noch nicht.</summary>
    [Fact]
    public void Auf_Stufe_zwei_kommt_die_Spanne_dazu()
    {
        var json = Draht(Stufe.Unterlagen);

        json.Should().Contain("\"salary_min\"");
        json.Should().NotContain("\"name\"");
        json.Should().NotContain("\"email\"");
    }

    /// <summary>Auf Stufe 3 steht alles da.</summary>
    [Fact]
    public void Auf_Stufe_drei_steht_alles_da()
    {
        var json = Draht(Stufe.Person);

        json.Should().Contain("\"salary_min\"");
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"email\"");
    }

    /// <summary>
    /// Und „freigegeben, aber nie ausgefüllt" sieht genauso aus wie „nicht
    /// freigegeben".
    /// </summary>
    /// <remarks>
    /// <para><strong>Die eigentliche Zusage aus ADR-0020 §1</strong>, hier auf
    /// Feldebene: verborgen und nicht vorhanden sind ununterscheidbar. Wer die
    /// zwei auseinanderhalten könnte, erführe aus dem Unterschied, dass die
    /// Person ein Gehalt <em>genannt</em> hat — eine Auskunft, die sie nicht
    /// gegeben hat.</para>
    ///
    /// <para>Verglichen werden die beiden JSON-Dokumente wörtlich, nicht Feld
    /// für Feld: eine Prüfung, die nur die genannten Felder vergleicht, übersähe
    /// genau das Feld, das jemand neu hinzufügt.</para>
    /// </remarks>
    [Fact]
    public void Freigegeben_ohne_Angabe_sieht_aus_wie_nicht_freigegeben()
    {
        var ohneFreigabe = Draht(Stufe.Profile, mandate: Voll());
        var freiAberLeer = Draht(Stufe.Unterlagen, mandate: Ohne_Gehalt());

        // Beide duerfen kein Gehaltsfeld tragen — aus zwei verschiedenen
        // Gruenden, und das darf man ihnen nicht ansehen.
        ohneFreigabe.Should().NotContain("salary");
        freiAberLeer.Should().NotContain("salary");
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 4 — DER JETZIGE ARBEITGEBER ERFÄHRT NICHTS
    // ---------------------------------------------------------------------

    /// <summary>
    /// Wer ein Unternehmen ausschliesst, gibt „für alle Unternehmen" auf.
    /// </summary>
    /// <remarks>
    /// <para><strong>Hier hängt die Abnahme.</strong> „Der jetzige Arbeitgeber
    /// sieht die eigene Belegschaft nicht im Scout" wird nicht durch ein zweites
    /// Tor im Scout eingelöst — das wäre eine zweite Job, an der über
    /// Sichtbarkeit entschieden wird — sondern im Ledger selbst.</para>
    ///
    /// <para>Der Ledger kennt keine Verneinung: „sichtbar für alle, aber nicht
    /// für X" ist darin nicht ausdrückbar. Also gibt es den Modus „alle" nicht
    /// mehr, sobald jemand ein X nennt. Danach findet der Scout diese Person nur
    /// noch für Unternehmen, denen sie einzeln freigegeben hat — und der
    /// ausgeschlossene ist keines davon.</para>
    /// </remarks>
    [Fact]
    public async Task Ein_Ausschluss_gibt_die_oeffentliche_Sichtbarkeit_auf()
    {
        var (_, mandate, tor, _) = Aufbau();
        tor.Selbst = Anna;
        tor.Oeffentlich(Anna, true);

        await new WriteMandateHandler(mandate, tor, TimeProvider.System).Handle(
            new WriteMandateCommand(
                new SubjectId(Anna), null, null, null, null, ["arbeitgeber.test"]),
            default);

        tor.Widerrufe.Should().ContainSingle()
            .Which.Should().Be(StageCapabilities.ProfilOeffentlich);

        tor.Gruende.Should().OnlyContain(
            reason => reason == WriteMandateHandler.Widerrufsgrund);
    }

    /// <summary>Ohne Ausschluss wird nichts widerrufen.</summary>
    /// <remarks>
    /// Die Gegenprobe: ein Mandate ohne Ausschluss darf die öffentliche
    /// Sichtbarkeit nicht anrühren. Wer sein Gehalt einträgt, verschwindet damit
    /// nicht aus der Search.
    /// </remarks>
    [Fact]
    public async Task Ohne_Ausschluss_bleibt_die_Sichtbarkeit_unangetastet()
    {
        var (_, mandate, tor, _) = Aufbau();
        tor.Selbst = Anna;
        tor.Oeffentlich(Anna, true);

        await new WriteMandateHandler(mandate, tor, TimeProvider.System).Handle(
            new WriteMandateCommand(new SubjectId(Anna), "2026-11", 4000, 5000, 80, null),
            default);

        tor.Widerrufe.Should().BeEmpty();
    }

    /// <summary>Ein ausgeschlossenes Unternehmen bekommt dieselbe 404 wie ein Fremder.</summary>
    /// <remarks>
    /// Kein eigener Code und keine eigene Meldung: „du stehst auf ihrer Liste"
    /// wäre eine Auskunft über einen Menschen an genau das Unternehmen, vor dem
    /// sie sich schützt.
    /// </remarks>
    [Fact]
    public async Task Ein_ausgeschlossenes_Unternehmen_bekommt_dieselbe_Antwort()
    {
        var (conversations, mandate, tor, _) = Aufbau();
        var companies = new Probefirmen();
        companies.Domains[Firma] = "arbeitgeber.test";

        tor.Job(Anna, Firma, Stufe.Profile);

        await mandate.SaveAsync(
            Mandate.Write(
                new SubjectId(Anna), null, null, null, null, ["arbeitgeber.test"],
                DateTimeOffset.UtcNow),
            default);

        var handler = new OpenConversationHandler(
            conversations, mandate, tor, companies, new Probeausgang(), TimeProvider.System);

        var tat = async () => await handler.Handle(
            new OpenConversationCommand(new SubjectId(Anna), new TenantId(Firma), "Hallo"),
            default);

        (await tat.Should().ThrowAsync<NoConversation>())
            .Which.Message.Should().Be("No such conversation");
    }

    /// <summary>Ohne Ausschluss wird identity-service gar nicht erst gefragt.</summary>
    /// <remarks>
    /// Nicht Sparsamkeit: ein Sprung, der in fast allen Fällen nichts
    /// entscheidet, ist ein Sprung, der bei jedem Ausfall des anderen Dienstes
    /// eine Eröffnung verhindert, die niemanden etwas angeht.
    /// </remarks>
    [Fact]
    public async Task Ohne_Ausschluss_wird_nicht_nach_der_Domain_gefragt()
    {
        var (conversations, mandate, tor, _) = Aufbau();
        var companies = new Probefirmen();

        tor.Job(Anna, Firma, Stufe.Profile);

        await new OpenConversationHandler(
                conversations, mandate, tor, companies, new Probeausgang(), TimeProvider.System)
            .Handle(
                new OpenConversationCommand(new SubjectId(Anna), new TenantId(Firma), "Hallo"),
                default);

        companies.Fragen.Should().Be(0);
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 5 — DER DREIECKSKONSENS WIRD NICHT NACHGEBAUT
    // ---------------------------------------------------------------------

    /// <summary>
    /// Dieser Dienst kennt keine der Bedingungen eines Transfers.
    /// </summary>
    /// <remarks>
    /// <para>Geprüft an den Typnamen und nicht am Verhalten: ein
    /// Verhaltenstest beantwortet „passiert es heute nicht?", dieser verlangt,
    /// dass wer eine Marktregel hierher kopiert, sie hier hinschreibt.</para>
    ///
    /// <para>Ansprechbarkeit, MarketStatus, Arbeitgeberfreigabe, Ablöse: alles
    /// das steht in transfer-service, und die Übergabe ruft dessen Tür. Eine
    /// zweite Job, die diese Regeln kennt, wäre die, die als Erste
    /// veraltet.</para>
    /// </remarks>
    [Fact]
    public void Keine_Marktregel_wird_hier_nachgebaut()
    {
        var verdaechtig = new[]
        {
            "ansprechbar", "approachable", "marktstatus", "verfuegbarkeit",
            "abloese", "gebuehr", "brauchtfreigabe"
        };

        var namen = Namen(typeof(OpenConversationCommand).Assembly)
            .Concat(Namen(typeof(Conversation).Assembly))
            .ToArray();

        namen.Should().NotBeEmpty("sonst prüft der Test nichts");

        foreach (var name in namen)
        {
            foreach (var word in verdaechtig)
            {
                name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{name}' baut eine Regel nach, die in transfer-service steht "
                        + "(ADR-0037 Entscheidung 4)");
            }
        }
    }

    /// <summary>Die Übergabe geht erst nach der Zustimmung der Person.</summary>
    [Fact]
    public async Task Uebergeben_geht_erst_nach_der_Zustimmung()
    {
        var (conversations, _, tor, _) = Aufbau();
        var conversation = await Eroeffne(conversations, tor);
        var handover = new Probeuebergabe();

        var handler = new UebergebenHandler(conversations, handover, TimeProvider.System);

        var zuFrueh = async () => await handler.Handle(
            new UebergebenBefehl(conversation.Id, new TenantId(Firma)), default);

        await zuFrueh.Should().ThrowAsync<UebergangNichtErlaubt>();
        handover.Uebergeben.Should().BeEmpty();

        await new ZustimmenHandler(conversations, tor, TimeProvider.System).Handle(
            new ZustimmenBefehl(conversation.Id, new SubjectId(Anna)), default);

        var caseItem = await handler.Handle(
            new UebergebenBefehl(conversation.Id, new TenantId(Firma)), default);

        caseItem.Should().Be(handover.Case);
        handover.Uebergeben.Should().Equal(Anna);
    }

    /// <summary>Lehnt transfer-service ab, bleibt das Gespräch stehen.</summary>
    /// <remarks>
    /// Erst schreiben, wenn es geklappt hat: andersherum stünde hier
    /// „übergeben" und dort nichts — und niemand fände den Case, den beide
    /// Seiten für gemacht halten.
    /// </remarks>
    [Fact]
    public async Task Eine_abgelehnte_Uebergabe_laesst_das_Gespraech_stehen()
    {
        var (conversations, _, tor, _) = Aufbau();
        var conversation = await Eroeffne(conversations, tor);

        await new ZustimmenHandler(conversations, tor, TimeProvider.System).Handle(
            new ZustimmenBefehl(conversation.Id, new SubjectId(Anna)), default);

        var handover = new Probeuebergabe { LehntAb = true };

        var tat = async () => await new UebergebenHandler(
                conversations, handover, TimeProvider.System)
            .Handle(new UebergebenBefehl(conversation.Id, new TenantId(Firma)), default);

        await tat.Should().ThrowAsync<HandoverRefused>();

        (await conversations.GetAsync(conversation.Id))!.State
            .Should().Be(ConversationState.Zugestimmt);
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 6 — DIE STUFE WIRD ERST NACH DEM LEDGER NACHGESCHLAGEN
    // ---------------------------------------------------------------------

    /// <summary>Über wen nichts freigegeben ist, über den wird nichts nachgeschlagen.</summary>
    /// <remarks>
    /// Die Reihenfolge ist die Zusage — dieselbe, die scout-service für Belege
    /// hält. Ein Abruf des Klarnamens für eine Person auf Stufe 1 wäre ein
    /// Aufruf, den es nicht geben darf, und er stünde im Protokoll des anderen
    /// Dienstes.
    /// </remarks>
    [Fact]
    public async Task Der_Klarname_wird_erst_ab_Stufe_drei_geholt()
    {
        var (conversations, mandate, tor, personen) = Aufbau();
        var conversation = await Eroeffne(conversations, tor);

        var handler = new CompanyConversationsHandler(conversations, mandate, tor, personen);

        await handler.Handle(new CompanyConversationsQuery(new TenantId(Firma)), default);
        personen.Gefragt.Should().BeEmpty("auf Stufe 1 gibt es keinen Klarnamen zu holen");

        tor.Job(Anna, Firma, Stufe.Person);
        personen.Bestand[Anna] = new Application.Ports.Personenbild("Anna Beispiel", "a@b.test");

        var ihre = await handler.Handle(
            new CompanyConversationsQuery(new TenantId(Firma)), default);

        personen.Gefragt.Should().Equal(Anna);
        ihre.Should().ContainSingle().Which.Name.Should().Be("Anna Beispiel");
        conversation.Wer.Value.Should().Be(Anna);
    }

    // ---------------------------------------------------------------------

    /// <summary>Die öffentlichen Instanzeigenschaften eines Typs.</summary>
    private static string[] Felder(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(e => e.Name)];

    private static string[] Namen(Assembly assembly) =>
        [.. assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(glied => $"{type.Name}.{glied.Name}")
                .Append(type.Name))
            .Distinct()];

    /// <summary>Jede Spalte, die dieses Modell wirklich anlegt.</summary>
    private static string[] Spalten()
    {
        var bauer = new DbContextOptionsBuilder<AdvisorDbContext>();
        AdvisorDbContextFactory.ZurEntwurfszeit(bauer, "Host=pruefstand;Database=advisor");

        using var context = new AdvisorDbContext(bauer.Options);

        return [.. context.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties())
            .Select(eigenschaft => eigenschaft.GetColumnName())
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Was für diese Stufe wirklich auf dem Draht steht.</summary>
    private static string Draht(Stufe stage, Mandate? mandate = null)
    {
        var conversation = Conversation.Eroeffne(
            new SubjectId(Anna), new TenantId(Firma), "Hallo", DateTimeOffset.UtcNow);

        var ansicht = ConversationView.Build(
            conversation,
            stage,
            mandate ?? Voll(),
            new Application.Ports.Personenbild("Anna Beispiel", "anna@beispiel.test"));

        return JsonSerializer.Serialize(new ConversationV1(
            ansicht.Conversation.Id,
            ansicht.Conversation.Wer.Value,
            ConversationStates.Wort(ansicht.Conversation.State),
            StageCapabilities.Zahl(ansicht.Stufe),
            ansicht.Conversation.Anlass,
            ansicht.Conversation.EroeffnetAm,
            ansicht.Conversation.GeaendertAm)
        {
            EntryMonth = ansicht.Eintrittstermin,
            WorkloadPercent = ansicht.PensumProzent,
            SalaryMin = ansicht.GehaltMin,
            SalaryMax = ansicht.GehaltMax,
            Name = ansicht.Name,
            Email = ansicht.Email
        });
    }

    private static Mandate Voll() =>
        Mandate.Write(
            new SubjectId(Anna), "2026-11", 4000, 5000, 80, [], DateTimeOffset.UtcNow);

    private static Mandate Ohne_Gehalt() =>
        Mandate.Write(
            new SubjectId(Anna), "2026-11", null, null, 80, [], DateTimeOffset.UtcNow);

    private static (Probegespraeche, Probemandate, Probetor, Probepersonen) Aufbau() =>
        (new Probegespraeche(), new Probemandate(), new Probetor(), new Probepersonen());

    private static async Task<Conversation> Eroeffne(Probegespraeche conversations, Probetor tor)
    {
        tor.Job(Anna, Firma, Stufe.Profile);

        var conversation = Conversation.Eroeffne(
            new SubjectId(Anna), new TenantId(Firma), "Hallo", DateTimeOffset.UtcNow);

        await conversations.SaveAsync(conversation);

        return conversation;
    }
}
