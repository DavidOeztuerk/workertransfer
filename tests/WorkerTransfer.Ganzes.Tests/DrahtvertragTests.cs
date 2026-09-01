using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Jeder zusammengesetzte Feldname auf dem Draht steht ausdrücklich da.
/// </summary>
/// <remarks>
/// <para><strong>Der Draht dieser Plattform ist snake_case.</strong> Neun Dienste
/// setzen ihn mit <c>[JsonPropertyName]</c> auf ihren Vertragstypen, und
/// <c>apps/web</c> liest und schreibt danach. Ein Rumpf <em>ohne</em> die
/// Angaben fällt aber auf camelCase aus <c>GirderModule.JsonOptions</c> zurück —
/// und dann heißt dasselbe Feld auf beiden Seiten anders.</para>
///
/// <para><strong>Warum das über Jahre unsichtbar bleibt.</strong> Bei einwortigen
/// Feldern sind camelCase und snake_case <em>dasselbe Wort</em>: <c>token</c>,
/// <c>email</c>, <c>capability</c>, <c>reason</c>. Erst ein zusammengesetzter
/// Name geht auseinander. Deshalb prüft diese Reihe genau die und nur die.</para>
///
/// <para><strong>Was es gekostet hat</strong>, vier Stellen, drei
/// Schadensbilder — alle gemessen, bevor sie behoben wurden:</para>
/// <list type="bullet">
/// <item><c>RegisterBody.DisplayName</c>: die Spalte ist <c>NOT NULL</c>, also
/// <c>500</c> (nach H4 <c>422</c>). Über die Oberfläche konnte sich
/// <strong>niemand registrieren</strong>.</item>
/// <item><c>ProfilKoerper.RemoteOk</c>: ein <c>bool</c> fällt still auf
/// <c>false</c>. Wer „Remote möglich" ankreuzte, bekam <c>200</c> — und das
/// Häkchen war weg.</item>
/// <item><c>GrantBody/RevokeBody/CheckBody.SubjectId</c>: kam als
/// <c>Guid.Empty</c> an, der Wächter verglich mit dem Aufrufer und antwortete
/// <c>403</c>. Über die Oberfläche war <strong>der Einwilligungs-Ledger
/// unbedienbar</strong> — der Dienst, auf den sich alle anderen stützen.</item>
/// </list>
///
/// <para><strong>Und warum kein Test es fand:</strong> die Reihen schickten
/// selbst camelCase. Wer gegen den Server prüft statt gegen den Vertrag,
/// bestätigt jeden Dialekt, den der Server gerade spricht — auch einen, den
/// sonst niemand spricht. 660 grüne Tests, und die Oberfläche kam nicht
/// hinein.</para>
///
/// <para>Geprüft wird an den <em>Typen</em> der geladenen Assemblies, nicht am
/// Quelltext: ein regulärer Ausdruck übersähe ein Feld, das aus einem
/// Basistyp kommt.</para>
/// </remarks>
public class DrahtvertragTests
{
    /// <summary>Je Dienst ein Typ aus seiner Api-Schicht.</summary>
    public static TheoryData<string, Type> ApiSchichten => new()
    {
        { "identity", typeof(Identity.Api.AuthEndpoints) },
        { "consent", typeof(Consent.Api.EinwilligungsEndpoints) },
        { "profile", typeof(Profile.Api.ProfilEndpoints) },
        { "resume", typeof(Resume.Api.LebenslaufEndpoints) },
        { "portfolio", typeof(Portfolio.Api.PortfolioEndpoints) },
        { "jobs", typeof(Jobs.Api.RueckzugsEndpoints) },
        { "applications", typeof(Applications.Api.LoeschEndpoints) },
        { "companies", typeof(Companies.Api.ArbeitgeberEndpoints) },
        { "transfer", typeof(Transfer.Api.VorgangsEndpoints) },
        { "github", typeof(GitHub.Api.LoeschEndpoints) },
        { "notification", typeof(Notification.Api.LoeschEndpoints) },

        // Die gemeinsamen Vertraege gehoeren dazu, und zwar dringend: hier hat
        // sich der Fehler zuletzt versteckt. `EinwilligungsfrageV1` reist
        // zwischen zwei Diensten, also war sie auf BEIDEN Seiten camelCase und
        // damit in sich stimmig — bis der Empfaenger auf den Draht der
        // Plattform kam und der Absender nicht. Gemessen: consent-service warf
        // `A SubjectId must not be empty`, profile-service meldete 503 „the
        // consent ledger did not answer", und die Kandidatenliste war leer.
        { "contracts.consent", typeof(Contracts.Consent.EinwilligungsfrageV1) },
        { "contracts.erasure", typeof(Contracts.Erasure.LoeschungV1) },

        // Und die Vertragsschicht JEDES Dienstes, die eine hat. Sie liegt in
        // einer EIGENEN Assembly — wer nur die Api-Schicht scannt, sieht sie
        // nicht. Genau daran ist `BenachrichtigenV1` vorbeigekommen.
        { "applications.contracts", typeof(Applications.Contracts.BewerbungV1) },
        { "companies.contracts", typeof(Companies.Contracts.ArbeitgeberprofilV1) },
        { "github.contracts", typeof(GitHub.Contracts.RepositoryV1) },
        { "jobs.contracts", typeof(Jobs.Contracts.StelleV1) },
        { "notification.contracts", typeof(Notification.Contracts.BenachrichtigenV1) },
        { "portfolio.contracts", typeof(Portfolio.Contracts.EintragV1) },
        { "resume.contracts", typeof(Resume.Contracts.StationV1) },
        { "transfer.contracts", typeof(Transfer.Contracts.MarktstatusV1) }
    };

    /// <summary>Ob camelCase und snake_case bei diesem Namen auseinandergehen.</summary>
    /// <remarks>
    /// Genau dann, wenn nach dem ersten Zeichen noch ein Grossbuchstabe kommt.
    /// <c>Token</c> geht nicht auseinander, <c>DisplayName</c> schon.
    /// </remarks>
    private static bool Zusammengesetzt(string name) =>
        name.Skip(1).Any(char.IsUpper);

    /// <summary>Kein Rumpftyp lässt einen zusammengesetzten Namen offen.</summary>
    [Theory]
    [MemberData(nameof(ApiSchichten))]
    public void Zusammengesetzte_Namen_stehen_ausdruecklich_da(string dienst, Type ausDerSchicht)
    {
        var offen = ausDerSchicht.Assembly
            .GetTypes()
            // ALLE Records, auch verschachtelte und private: `MeldungV1` in
            // identity ist ein `private sealed record` innerhalb der
            // Endpunktklasse, und genau den hat ein Filter auf `IsPublic`
            // uebersehen — waehrend er auf dem Draht ganz normal gebunden wird.
            .Where(typ => !typ.IsAbstract)
            // Rumpftypen sind hier Records; das erkennt man am erzeugten Klon.
            .Where(typ => typ.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null)
            .SelectMany(typ => typ.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(eigenschaft => Zusammengesetzt(eigenschaft.Name))
                .Select(eigenschaft => (
                    Name: $"{typ.Name}.{eigenschaft.Name}",
                    Draht: eigenschaft.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
                // Fehlt die Angabe, oder steht ein Grossbuchstabe darin, ist es
                // nicht der Draht dieser Plattform. Die zweite Haelfte ist
                // nicht Kosmetik: `BenachrichtigenV1` TRUG die Angabe — mit dem
                // Wert "userId". Ein Waechter, der nur Anwesenheit prueft,
                // haette das durchgewinkt und den zweiten Dialekt festgeschrieben.
                .Where(eintrag => eintrag.Draht is null || eintrag.Draht.Any(char.IsUpper))
                .Select(eintrag => eintrag.Draht is null
                    ? eintrag.Name
                    : $"{eintrag.Name} (steht als \"{eintrag.Draht}\" auf dem Draht)"))
            .ToArray();

        offen.Should().BeEmpty(
            "{0} traegt einen zusammengesetzten Feldnamen ohne [JsonPropertyName]. "
            + "Ohne ihn heisst das Feld auf dem Draht camelCase, die Oberflaeche "
            + "schickt snake_case, und der Wert kommt nie an", dienst);
    }

    /// <summary>Die Liste deckt alle elf Api-Schichten und beide Vertraege ab.</summary>
    [Fact]
    public void Die_Liste_deckt_alle_Dienste_ab()
    {
        var dienste = ApiSchichten.Select(zeile => (string)zeile[0]!).ToArray();

        dienste.Should().HaveCount(21);
        dienste.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Die Erkennung selbst stimmt — sonst wäre die Reihe grün, weil sie nichts
    /// findet.
    /// </summary>
    [Theory]
    [InlineData("Token", false)]
    [InlineData("Email", false)]
    [InlineData("Capability", false)]
    [InlineData("DisplayName", true)]
    [InlineData("SubjectId", true)]
    [InlineData("RemoteOk", true)]
    public void Die_Erkennung_trennt_richtig(string name, bool erwartet) =>
        Zusammengesetzt(name).Should().Be(erwartet);
}
