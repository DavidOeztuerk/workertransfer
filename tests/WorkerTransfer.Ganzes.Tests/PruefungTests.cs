using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Infrastructure.Einwilligung;
using WorkerTransfer.ServiceDefaults.Checks;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Was dieses Feld trägt, ist der Gegenversuch — und er übersetzt.
/// </summary>
/// <remarks>
/// <para>Er stand in PR #87 und hat den Umstieg überlebt, weil die Prüfung ihn
/// überlebt hat: <c>wt.ki.keine-zahl</c> hat in Noelia keine Entsprechung.
/// Geändert hat sich die Basisklasse, nicht der Inhalt.</para>
///
/// <para><strong>Ein echter Typ und kein Name in einer Zeichenkette.</strong>
/// Ein Gegenversuch, der die Prüfung mit <c>"Passung"</c> füttert, prüft die
/// Zeichenkettensuche; dieser prüft, dass die Absuche über eine Assembly
/// wirklich bei den Feldern ankommt.</para>
/// </remarks>
public sealed record KanarienvogelV1(string Name, int Passung);

/// <summary>Ein Ledger-Tor mit einem Zwischenspeicher davor — der Gegenversuch.</summary>
/// <remarks>
/// Er übersetzt, und das ist die halbe Miete: <c>else if (false)</c> wäre
/// unerreichbarer Code und damit ein <em>Bau</em>fehler, und ein Baufehler liest
/// sich in der Ausgabe wie ein bestandener Test.
/// </remarks>
public sealed class GemerktesTor(IConsentGate dahinter) : IConsentGate
{
    private bool? _gemerkt;

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        Noelia.Core.Identity.SubjectId subject,
        Noelia.Core.Identity.TenantId company,
        CancellationToken cancellationToken = default) =>
        _gemerkt ??= await dahinter.MaySeeAsync(subject, company, cancellationToken);
}

/// <summary>Die sechs Prüfungen, die Noelia nicht hat — und jede kann rot werden.</summary>
/// <remarks>
/// <para><strong>Was hier NICHT mehr steht, ist der Läufer.</strong> Bis
/// ADR-0045 fuhr ein eigener <c>Nachweislauf</c> die Prüfungen, fing jede
/// Ausnahme und setzte einen festen Satz ein. Noelias
/// <c>SecurityCheckRunner</c> tut dasselbe, und das ist <em>nachgelesen</em>
/// und nicht gehofft: er hat eine Zeitgrenze je Prüfung, er macht aus einer
/// Ausnahme ein <c>Fail</c>, und im Quelltext steht der Grund, den wir selbst
/// geschrieben hatten — <em>„Never copy an exception or its message: providers
/// often put an endpoint, connection string, key name or token in one."</em>
/// Zwei Läufer für dieselbe Aufgabe wären zwei Wahrheiten; unserer ist
/// gelöscht.</para>
///
/// <para><strong>Zu jeder Zusage ein Gegenversuch</strong>, und jeder ist
/// gefahren worden, bevor er hier steht.</para>
/// </remarks>
public class PruefungTests
{
    // ------------------------------------------------------- wt.ki.keine-zahl

    /// <summary>Über Domäne und Verträgen von profile-service trägt kein Name eine Zahl.</summary>
    [Fact]
    public async Task Die_Zahlpruefung_findet_in_profile_service_nichts()
    {
        var finding = await new NumberCheck(
            [typeof(Profile.Domain.Profile.PersonProfile).Assembly,
             typeof(Profile.Contracts.ProfileHitV1).Assembly]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Pass);
        finding.Id.Should().Be("wt.ki.keine-zahl");
    }

    /// <summary>Und sie wird rot, sobald ein Vertrag ein Feld <c>Passung</c> trägt.</summary>
    [Fact]
    public async Task Die_Zahlpruefung_wird_rot_wenn_ein_Vertrag_eine_Passung_traegt()
    {
        var finding = await new NumberCheck([typeof(KanarienvogelV1).Assembly]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("KanarienvogelV1.Passung");
    }

    /// <summary>Eine leere Fläche ist kein grüner Haken.</summary>
    [Fact]
    public async Task Die_Zahlpruefung_meldet_nicht_gruen_ueber_nichts()
    {
        var finding = await new NumberCheck([]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("nichts angesehen");
    }

    /// <summary>§ 87 Abs. 1 Nr. 6 BetrVG reist im Text mit, weil er als Zitat nicht geht.</summary>
    /// <remarks>
    /// <strong>Die Lücke steht sichtbar statt verschwiegen.</strong>
    /// <c>RegulatoryRegime</c> kennt kein nationales Arbeitsrecht
    /// (<c>bugs/betrvg-hat-kein-regelwerk.md</c>). Ihn auf <c>Gdpr</c> zu legen
    /// wäre eine falsche Fundstelle in einem Dokument, das ein Betriebsrat
    /// liest — schlimmer als keine.
    /// </remarks>
    [Fact]
    public async Task Die_Mitbestimmung_steht_im_Text_und_nicht_als_falsches_Zitat()
    {
        var finding = await new NumberCheck([typeof(KanarienvogelV1).Assembly]).RunAsync();

        finding.Remediation.Should().Contain("§ 87 Abs. 1 Nr. 6 BetrVG");

        finding.References.Should().NotContain(
            bezug => bezug.Article.Contains("87", StringComparison.Ordinal),
            "ein Arbeitsrechtsartikel unter einem DSGVO-Regelwerk wäre eine "
            + "falsche Fundstelle");
    }

    // ------------------------------------------------------------- wt.ki.naht

    /// <summary>Die vereinbarte Feldmenge steht, und sie steht im Befund.</summary>
    [Fact]
    public async Task Die_Nahtpruefung_nennt_die_vier_Felder_und_den_Prompt()
    {
        var finding = await new SeamCheck(
            typeof(DraftSituation),
            ["Ueberschrift", "Text", "Faehigkeiten", "Wunsch", "Prompt"],
            "für einen Profiltext").RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Pass);
        finding.Summary.Should().Contain("Wunsch").And.Contain("Prompt");
    }

    /// <summary>Ein Feld mehr als vereinbart: rot.</summary>
    [Fact]
    public async Task Die_Nahtpruefung_wird_rot_wenn_ein_Feld_dazukommt()
    {
        var finding = await new SeamCheck(
            typeof(DraftSituation),
            ["Ueberschrift", "Text", "Faehigkeiten", "Prompt"],
            "für einen Profiltext").RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("zusätzlich Wunsch");
    }

    // --------------------------------------------------------- wt.ki.anbieter

    /// <summary>Ohne Eintrag ist der Gegenstand nicht da — und das ist kein Haken.</summary>
    [Fact]
    public async Task Die_Anbieterpruefung_meldet_ohne_Eintrag_NichtAnwendbar()
    {
        var finding = await new ProviderCheck([]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.NotApplicable);
        finding.Status.Should().NotBe(
            SecurityCheckStatus.Pass,
            "ein grüner Haken an etwas, das gar nicht gilt, ist Rauschen in "
            + "genau dem Dokument, das Rauschen durchschneiden soll");
    }

    /// <summary>Sie zählt Menschen und nennt keinen.</summary>
    /// <remarks>
    /// <strong>Das ist die Prüfung, die Noelia nicht haben kann.</strong> Sein
    /// <c>noelia.ai.inventory</c> liest die zur STARTZEIT deklarierten
    /// Abhängigkeiten; der Zugang steht hier je Person in einer Tabelle.
    /// </remarks>
    [Fact]
    public async Task Die_Anbieterpruefung_zaehlt_Menschen_statt_sie_zu_nennen()
    {
        var finding = await new ProviderCheck(
        [
            new Probequelle(
            [
                new ProviderRow("anthropic", "api.anthropic.com", ProviderOrigin.Person, 2),
                new ProviderRow("anthropic", "api.anthropic.com", ProviderOrigin.Person, 1),
                new ProviderRow("openai_compatible", "localhost", ProviderOrigin.Person, 1)
            ])
        ]).RunAsync();

        finding.Summary.Should().Contain("3 Mensch(en) auf api.anthropic.com");
        finding.Summary.Should().Contain("eigenes Netz");
        finding.Status.Should().Be(SecurityCheckStatus.Warning);
    }

    /// <summary>Nicht eingerichtet heißt: keine Zeile, nicht eine leere.</summary>
    [Fact]
    public async Task Ein_nicht_eingerichteter_Betreiberanbieter_liefert_keine_Zeile() =>
        (await new OperatorProviderSource(
            "anthropic", "https://api.anthropic.com/v1/messages", eingerichtet: false)
            .ReadAsync()).Should().BeEmpty();

    // ---------------------------------------------------- wt.einwilligung.wirkt

    /// <summary>Zwischen Frage und Ledger steht der Adapter selbst.</summary>
    [Fact]
    public async Task Die_Widerrufspruefung_ist_gruen_ohne_Zwischenstueck()
    {
        await using var provider = Profilbau().BuildServiceProvider();

        var finding = await new RevocationCheck<IConsentGate>(
            provider, typeof(HttpConsentGate)).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Pass);
        finding.Summary.Should().Contain("kein weiterer Typ");
    }

    /// <summary>Und rot, sobald jemand einen Zwischenspeicher davorsetzt.</summary>
    [Fact]
    public async Task Die_Widerrufspruefung_wird_rot_mit_einem_Zwischenspeicher()
    {
        var services = Profilbau();

        services.AddScoped<IConsentGate>(provider =>
            new GemerktesTor(
                ActivatorUtilities.CreateInstance<HttpConsentGate>(provider)));

        await using var provider = services.BuildServiceProvider();

        var finding = await new RevocationCheck<IConsentGate>(
            provider, typeof(HttpConsentGate)).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("GemerktesTor");
        finding.Remediation.Should().Contain("ADR-0013");
    }

    // --------------------------------------------------- wt.loeschung.nachweis

    /// <summary>Eine verschlossene Tür ist eine nicht eingelöste Löschzusage.</summary>
    [Fact]
    public async Task Eine_geschlossene_Loeschtuer_wird_rot()
    {
        var finding = await ErasureCheck.AlsEmpfaenger(
            "profile", tuerEingerichtet: false, pruefspurVorhanden: true).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("Tür ist");
    }

    /// <summary>Ein Dienst ohne Personenzeilen ist kein Empfänger — und kein Haken.</summary>
    [Fact]
    public async Task Ein_Dienst_ohne_Personenzeilen_meldet_NichtAnwendbar() =>
        (await ErasureCheck.OhneZeilen("jobs").RunAsync())
            .Status.Should().Be(SecurityCheckStatus.NotApplicable);

    /// <summary>Offene Tür, aber keine Prüfspur: erreichbar, unbelegt.</summary>
    [Fact]
    public async Task Ohne_Pruefspur_bleibt_ein_Hinweis()
    {
        var finding = await ErasureCheck.AlsEmpfaenger(
            "portfolio", tuerEingerichtet: true, pruefspurVorhanden: false).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Warning);
    }

    /// <summary>Der Ursprung kennt jede Adresse — oder er sagt, welche fehlt.</summary>
    [Fact]
    public async Task Ein_Ursprung_ohne_Adresse_wird_rot()
    {
        var finding = await ErasureCheck.AlsUrsprung(
            "identity", ["consent", "profile"], ["consent"]).RunAsync();

        finding.Status.Should().Be(SecurityCheckStatus.Fail);
        finding.Summary.Should().Contain("profile");
    }

    // ------------------------------------------------------------- Werkzeug

    private sealed class Probequelle(IReadOnlyList<ProviderRow> rows) : IProviderSource
    {
        public Task<IReadOnlyList<ProviderRow>> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult(rows);
    }

    /// <summary>Der Verbundpunkt von profile-service, ohne Datenbank am Draht.</summary>
    private static ServiceCollection Profilbau()
    {
        var services = new ServiceCollection();

        services.AddHttpClient();
        // Das Tor fragt IM AUFTRAG DES AUFRUFERS und braucht dafuer dessen
        // ResumeRequest — ohne diese Zeile laesst es sich gar nicht bauen, und die
        // Pruefung meldete `Fail` ueber einen Fehler dieser Vorrichtung statt
        // ueber den Baum.
        services.AddHttpContextAccessor();
        services.AddOptions<ConsentSettings>();
        services.AddScoped<IConsentGate, HttpConsentGate>();

        return services;
    }
}
