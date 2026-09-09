using System.Net;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Contracts;
using WorkerTransfer.Notification.Infrastructure.Post;
using AnwendungenPost = WorkerTransfer.Applications.Infrastructure.Benachrichtigung;
using LebenslaufPost = WorkerTransfer.Resume.Infrastructure.Benachrichtigung;
using WechselPost = WorkerTransfer.Transfer.Infrastructure.Benachrichtigung;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Was drei Dienste an <c>notification-service</c> schicken, muss dieser auch
/// lesen können.
/// </summary>
/// <remarks>
/// <para><strong>Der Anlass.</strong> Alle drei schickten <c>userId</c>,
/// der Empfänger deklariert <c>[JsonPropertyName("user_id")]</c>. Die Kennung
/// band still auf <c>Guid.Empty</c>, der Dienst antwortete <b>500</b>, und weil
/// der Versand ein <c>EnsureSuccessStatusCode()</c> hat, gab der Ausgangskorb
/// nach zehn Versuchen auf. Gemessen am laufenden Stapel: 18 Zeilen, keine
/// einzige zugestellt, im ganzen Postfach nur Bestätigungsmails. <b>Der
/// Benachrichtigungsweg hat nie funktioniert.</b></para>
///
/// <para><strong>Warum dieser Test und nicht ein Namensvergleich.</strong> Ein
/// Test, der „heisst das Feld user_id?" fragt, prüft dieselbe Zeichenkette
/// zweimal und geht mit ihr gemeinsam kaputt. Hier wird stattdessen der Rumpf
/// abgefangen, den der Absender WIRKLICH schreibt, und in den Vertrag des
/// Empfängers deserialisiert. Grün heisst dann: die Kennung kommt an. Das ist
/// die Zusage, und sie überlebt jede Umbenennung.</para>
///
/// <para><strong>Warum es keine andere Reihe fängt.</strong>
/// <c>DrahtvertragTests</c> liest deklarierte Eigenschaften in den Api- und
/// Contracts-Baugruppen. Hier steht der Rumpf als <em>anonymes Objekt</em> in
/// der Infrastruktur — kein deklarierter Typ, also nichts zu prüfen. Und keine
/// Dienstreihe kreuzt die Naht: der Absender hat im Test keinen Empfänger, der
/// Empfänger keinen Absender.</para>
/// </remarks>
public class BenachrichtigungsdrahtTests
{
    private static readonly Guid Empfaenger = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>Der Rumpf von transfer-service.</summary>
    [Fact]
    public Task Transfer_schickt_eine_Kennung_die_ankommt() =>
        Pruefe(async (fabrik, einstellungen) =>
            await new WechselPost.HttpBenachrichtigung(
                fabrik,
                Options.Create(new WechselPost.Benachrichtigungseinstellungen
                {
                    Adresse = einstellungen,
                    Geheimnis = "probe"
                }))
                .ZustelleAsync(new SubjectId(Empfaenger), "market_request"));

    /// <summary>Der Rumpf von applications-service.</summary>
    [Fact]
    public Task Bewerbungen_schicken_eine_Kennung_die_ankommt() =>
        Pruefe(async (fabrik, einstellungen) =>
            await new AnwendungenPost.HttpBenachrichtigung(
                fabrik,
                Options.Create(new AnwendungenPost.Benachrichtigungseinstellungen
                {
                    Adresse = einstellungen,
                    Geheimnis = "probe"
                }))
                .ZustelleAsync(new SubjectId(Empfaenger), "application_status"));

    /// <summary>Der Rumpf von resume-service.</summary>
    [Fact]
    public Task Lebenslauf_schickt_eine_Kennung_die_ankommt() =>
        Pruefe(async (fabrik, einstellungen) =>
            await new LebenslaufPost.HttpBenachrichtigung(
                fabrik,
                Options.Create(new LebenslaufPost.Benachrichtigungseinstellungen
                {
                    Adresse = einstellungen,
                    Geheimnis = "probe"
                }))
                .ZustelleAsync(new SubjectId(Empfaenger), "resume_request"));

    /// <summary>
    /// Und der vierte Sprung: <c>notification-service</c> bittet
    /// <c>identity-service</c>, die Mail wirklich zu verschicken.
    /// </summary>
    /// <remarks>
    /// Der stillste der vier. Die drei oberen laufen über den Ausgangskorb und
    /// hinterlassen bei einem Fehlschlag zehn Versuche und eine Fehlerspalte;
    /// dieser wirft nicht, sondern protokolliert eine Warnung. Er kann also
    /// beliebig lange kaputt sein, ohne dass irgendwo etwas rot wird — und er
    /// war es: im Postfach standen nur die Bestätigungsmails, die
    /// <c>identity-service</c> selbst verschickt.
    /// <para>
    /// Der Empfängertyp <c>MeldungV1</c> ist <b>privat</b>. Er wird deshalb über
    /// Reflexion geholt statt nachgebaut: ein nachgebauter Vertrag im Test wäre
    /// eine zweite Wahrheit, und die beiden gingen beim nächsten Umbenennen
    /// getrennte Wege — genau die Trennung, die dieser Test aufdecken soll.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Benachrichtigung_bittet_identity_mit_einer_Kennung_die_ankommt()
    {
        var lauscher = new Lauscher();

        await new HttpPostbote(
            new Stubfabrik(lauscher),
            Options.Create(new Posteinstellungen
            {
                Adresse = "http://probe.invalid",
                Geheimnis = "probe"
            }),
            NullLogger<HttpPostbote>.Instance)
            .SchickeAsync(new SubjectId(Empfaenger));

        lauscher.Rumpf.Should().NotBeNull("es wurde nichts geschickt");

        var vertrag = typeof(WorkerTransfer.Identity.Api.MeldeEndpoints).Assembly
            .GetTypes()
            .Single(typ => typ.Name == "MeldungV1");

        var gelesen = JsonSerializer.Deserialize(lauscher.Rumpf!, vertrag);

        gelesen.Should().NotBeNull();
        vertrag.GetProperty("UserId")!.GetValue(gelesen).Should().Be(
            Empfaenger,
            "identity liest den Namen, den MeldungV1 deklariert — kommt die "
            + "Kennung anders, bindet sie still auf Guid.Empty und es geht keine "
            + "Mail hinaus, ohne dass irgendetwas rot wird");
    }

    /// <summary>
    /// Fängt den Rumpf ab und liest ihn mit dem Vertrag des Empfängers.
    /// </summary>
    /// <remarks>
    /// Deserialisiert wird OHNE eigene Optionen: genau so liest ihn ASP.NET am
    /// Endpunkt. Wer hier `PropertyNameCaseInsensitive` setzte, machte den Test
    /// nachsichtiger als die Wirklichkeit — und `userId` käme wieder durch.
    /// </remarks>
    private static async Task Pruefe(Func<IHttpClientFactory, string, Task> senden)
    {
        var lauscher = new Lauscher();

        await senden(new Stubfabrik(lauscher), "http://probe.invalid");

        lauscher.Rumpf.Should().NotBeNull("es wurde nichts geschickt");

        var gelesen = JsonSerializer.Deserialize<BenachrichtigenV1>(lauscher.Rumpf!);

        gelesen.Should().NotBeNull();
        gelesen!.UserId.Should().Be(
            Empfaenger,
            "der Empfaenger liest `user_id` — kommt die Kennung als etwas anderes, "
            + "bindet sie still auf Guid.Empty und der Dienst antwortet 500");
        gelesen.Kind.Should().NotBeNullOrEmpty("ohne Art gibt es 422");
    }

    /// <summary>Nimmt den Rumpf entgegen und bestätigt wie der echte Endpunkt.</summary>
    private sealed class Lauscher : HttpMessageHandler
    {
        public string? Rumpf { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Rumpf = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class Stubfabrik(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
