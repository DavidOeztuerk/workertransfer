using FluentAssertions;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults.Pruefungen;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Der Bauzeit-Zwilling von <c>wt.ki.keine-zahl</c>, über alle vierzehn Dienste.
/// </summary>
/// <remarks>
/// <para><strong>Warum beides.</strong> Die Prüfung im Nachweis sagt einem
/// Menschen, was in <em>dieser Instanz</em> läuft, und sie sagt es datiert und
/// in einem Dokument. Dieser Test hier fällt, <em>bevor</em> etwas ausgeliefert
/// wird — und er fällt auch in einem Dienst, dessen Nachweis gerade niemand
/// abruft. Die Prüfung berichtet, der Test hält auf.</para>
///
/// <para><strong>Und er deckt die Dienste ab, deren Nachweis eine Datenbank
/// braucht.</strong> Das ist der eigentliche Grund für diese Datei:
/// identity-service kann seinen Anbieterbefund nur mit einer Tabelle
/// beantworten; sein Wortschatzbefund braucht keine, und hier bekommt er sie
/// auch nicht.</para>
///
/// <para><strong>Eine Ausnahme existiert genau einmal.</strong>
/// <c>AdvisorInfrastructure.Wortausnahmen</c> ist öffentlich und wird von
/// beiden gelesen. Zwei Listen über dieselbe Frage laufen auseinander, und die
/// stillere von beiden wächst.</para>
/// </remarks>
public class WortschatzTests
{
    /// <summary>Die vierzehn Paare aus Domäne und Verträgen, wie die Dienste sie führen.</summary>
    /// <remarks>
    /// <c>identity</c> und <c>consent</c> zeigen auf die <em>geteilten</em>
    /// Vertragspakete: ihre eigenen <c>.Contracts</c>-Projekte sind leer, die
    /// Grenz-DTOs liegen unter <c>src/shared/</c>. Wer hier das leere Projekt
    /// einträgt, prüft eine Hälfte und meldet grün über die andere.
    /// </remarks>
    public static IReadOnlyList<(string Dienst, Type Domaene, Type Vertraege)> Paare { get; } =
    [
        ("identity", typeof(Identity.Domain.Audit.AuditAction),
         typeof(Contracts.Identity.KiZugangV1)),
        ("consent", typeof(Consent.Domain.Audit.AuditAction),
         typeof(Contracts.Consent.EinwilligungsfrageV1)),
        ("profile", typeof(Profile.Domain.Profile.Profil),
         typeof(Profile.Contracts.ProfilfundV1)),
        ("resume", typeof(Resume.Domain.Anfragen.Anfragestand),
         typeof(Resume.Contracts.StationV1)),
        ("portfolio", typeof(Portfolio.Domain.Ablage.Abgelegtes),
         typeof(Portfolio.Contracts.EintragV1)),
        ("jobs", typeof(Jobs.Domain.Stellen.Faehigkeitenliste),
         typeof(Jobs.Contracts.StelleV1)),
        ("applications", typeof(Applications.Domain.Bewerbungen.Bewerbungsstand),
         typeof(Applications.Contracts.BewerbungV1)),
        ("companies", typeof(Companies.Domain.Arbeitgeberprofile.Arbeitgeberprofil),
         typeof(Companies.Contracts.ArbeitgeberprofilV1)),
        ("transfer", typeof(Transfer.Domain.Anfragen.Anfragestand),
         typeof(Transfer.Contracts.MarktstatusV1)),
        ("notification", typeof(Notification.Domain.Benachrichtigungen.Benachrichtigungsart),
         typeof(Notification.Contracts.BenachrichtigenV1)),
        ("github", typeof(GitHub.Domain.Verbindungen.Loginfehler),
         typeof(GitHub.Contracts.RepositoryV1)),
        ("scout", typeof(Scout.Domain.Suchen.Suche),
         typeof(Scout.Contracts.HakenV1)),
        ("advisor", typeof(Advisor.Domain.Gespraeche.UebergangNichtErlaubt),
         typeof(Advisor.Contracts.MandatV1)),
        ("assessment", typeof(Assessment.Domain.Vorgaenge.Eingabefehler),
         typeof(Assessment.Contracts.AufgabeStellenV1))
    ];

    /// <summary>Dieselben vierzehn, wie xUnit sie aufzählt.</summary>
    /// <remarks>
    /// Eine Liste, zwei Formen — nicht zwei Listen. Die zweite wäre die, die
    /// beim fünfzehnten Dienst zurückbleibt.
    /// </remarks>
    public static TheoryData<string, Type, Type> Dienste
    {
        get
        {
            var daten = new TheoryData<string, Type, Type>();

            foreach (var (dienst, domaene, vertraege) in Paare)
            {
                daten.Add(dienst, domaene, vertraege);
            }

            return daten;
        }
    }

    /// <summary>
    /// In keiner Domäne und in keinem Vertrag kündigt ein Name eine Zahl über
    /// einen Menschen an.
    /// </summary>
    [Theory]
    [MemberData(nameof(Dienste))]
    public async Task Kein_Dienst_traegt_ein_Wort_das_einen_Menschen_bewertet(
        string dienst, Type domaene, Type vertraege)
    {
        var befund = await new Zahlpruefung(
            [domaene.Assembly, vertraege.Assembly], Ausnahmen(dienst)).RunAsync();

        befund.Status.Should().Be(
            SecurityCheckStatus.Pass,
            $"{dienst}: {befund.Summary}");
    }

    /// <summary>Die Erkennung selbst stimmt — sonst wäre alles grün, weil nichts trifft.</summary>
    /// <remarks>
    /// Die Gegenprobe zu den vierzehn oben. Ohne sie bliebe offen, ob der Test
    /// misst oder nur nicht findet — dieselbe Frage, die
    /// <c>scripts/test-dotnet.sh</c> mit seiner Zahl auf dem Schirm beantwortet.
    /// </remarks>
    [Theory]
    [InlineData("Haken", false)]
    [InlineData("Belegstand", false)]
    [InlineData("Seitenlaenge", false)]
    [InlineData("Capability", false)]
    [InlineData("Benefits", false)]
    [InlineData("Availability", false)]
    [InlineData("Passungsgrad", true)]
    [InlineData("PassungsGrad", true)]
    [InlineData("WorkloadPercent", true)]
    public void Die_Erkennung_trifft_genau_das_Gemeinte(string name, bool erwartet) =>
        // Ueber DIESELBE Regel, die die Pruefung fahert, und nicht ueber eine
        // nachgebaute: eine zweite Regel neben der ersten ist die, die als
        // Erste falsch wird.
        Zahlpruefung.Trifft(name).Should().Be(erwartet);

    /// <summary>
    /// Und was sie NICHT findet, steht ebenfalls fest — damit niemand mehr glaubt.
    /// </summary>
    /// <remarks>
    /// Ein deutsches Kompositum, das das Wort am <em>Ende</em> trägt, entgeht
    /// dem Silbenanfang: <c>Trefferanzahl</c> ist eine Silbe und beginnt nicht
    /// mit <c>anzahl</c>. Das hier festzuhalten ist der ehrlichere Weg, als es
    /// zu verschweigen — <strong>der Name ist das Signal, nicht die Regel</strong>,
    /// und die geschlossenen Feldmengen stehen daneben.
    /// </remarks>
    [Theory]
    [InlineData("Trefferanzahl")]
    [InlineData("Gesamtpunkte")]
    public void Ein_Kompositum_mit_dem_Wort_am_Ende_entgeht_ihr(string name) =>
        Zahlpruefung.Trifft(name).Should().BeFalse(
            "der Silbenanfang findet es nicht — wer sich darauf verlässt, "
            + "verlässt sich auf zu wenig");

    /// <summary>
    /// Dreizehn Ausnahmen im ganzen Baum — drei Dienste, drei Homonyme, jede mit Grund.
    /// </summary>
    /// <remarks>
    /// <strong>Die Zahl steht hier, damit eine siebte auffällt.</strong> Eine
    /// Ausnahmeliste wächst, solange niemand sie zählt; diese wird gezählt, und
    /// wer sie erweitert, ändert diesen Test in demselben Commit.
    /// </remarks>
    [Fact]
    public void Es_gibt_dreizehn_Ausnahmen_und_keine_ohne_Grund()
    {
        var alle = Alle();

        alle.Should().HaveCount(13);
        alle.Should().OnlyContain(ausnahme => ausnahme.Grund.Length > 20);
        alle.Select(ausnahme => ausnahme.Name).Should().OnlyHaveUniqueItems();
    }

    /// <summary>Die Ausnahmen zeigen auf Namen, die es wirklich gibt.</summary>
    /// <remarks>
    /// Eine Ausnahme für einen Namen, den es nicht mehr gibt, ist ein Freibrief,
    /// den niemand mehr braucht und den niemand zurückzieht. Die Prüfung selbst
    /// geht darauf rot — dieser Test sagt es zur Bauzeit, und das ist der
    /// Unterschied zwischen „fällt beim nächsten Abruf auf" und „fällt jetzt
    /// auf".
    /// </remarks>
    [Fact]
    public async Task Keine_Ausnahme_zeigt_ins_Leere()
    {
        foreach (var (dienst, domaene, vertraege) in Paare)
        {
            var befund = await new Zahlpruefung(
                [domaene.Assembly, vertraege.Assembly], Ausnahmen(dienst))
                .RunAsync();

            befund.Summary.Should().NotContain(
                "zeigen auf Namen", $"{dienst}: {befund.Summary}");
        }
    }

    /// <summary>Alle Ausnahmen des Baumes, aus den drei Quellen, die sie halten.</summary>
    private static IReadOnlyList<Wortausnahme> Alle() =>
    [
        .. Advisor.Infrastructure.AdvisorInfrastructure.Wortausnahmen,
        .. Applications.Infrastructure.ApplicationsInfrastructure.Wortausnahmen,
        .. Transfer.Infrastructure.TransferInfrastructure.Wortausnahmen
    ];

    /// <summary>Die Ausnahmen eines Dienstes — aus seinem Verbundpunkt, nicht von hier.</summary>
    private static IReadOnlyList<Wortausnahme> Ausnahmen(string dienst) => dienst switch
    {
        "advisor" => Advisor.Infrastructure.AdvisorInfrastructure.Wortausnahmen,
        "applications" => Applications.Infrastructure.ApplicationsInfrastructure.Wortausnahmen,
        "transfer" => Transfer.Infrastructure.TransferInfrastructure.Wortausnahmen,
        _ => []
    };
}
