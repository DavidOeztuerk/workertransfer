using FluentAssertions;
using Girder.Infrastructure.Sovereignty;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Was Girders Egress-Politik mit UNSEREN Adressen macht — gemessen, bevor sie
/// irgendwo registriert wird.
/// </summary>
/// <remarks>
/// <para><strong>Der Grund für diese Reihe ist ein Beinahe-Ausfall.</strong>
/// <c>AddSovereignPlatform</c> hängt <c>EgressGuardHandler</c> an <em>jeden</em>
/// Klienten aus der <c>IHttpClientFactory</c>, und der Wächter <em>weist ab</em>
/// statt zu protokollieren. Unsere Dienste rufen einander unter Containernamen
/// — <c>http://consent-service:8002</c>, nicht unter einer IP. Die Vorgabe
/// erlaubt „loopback und RFC1918"; ob ein <em>Name</em> darunter fällt,
/// entscheidet über die halbe Plattform und steht in keiner Zusammenfassung.
/// </para>
/// <para>Deshalb steht die Antwort hier als Messung und nicht als Annahme —
/// und sie bleibt stehen, damit eine spätere Girder-Fassung sie nicht
/// stillschweigend ändert.</para>
/// </remarks>
public sealed class EgressTests
{
    /// <summary>Die Vorgabe, wie <c>AddSovereignPlatform</c> sie baut.</summary>
    private static IEgressPolicy Vorgabe() =>
        Gebaut(bauer => bauer.AllowLoopback().AllowPrivateNetworks());

    private static IEgressPolicy Gebaut(Action<EgressPolicyBuilder> aufbau)
    {
        var bauer = new EgressPolicyBuilder();
        aufbau(bauer);

        return bauer.Build();
    }

    /// <summary>
    /// <strong>Ein Containername ist weder Loopback noch RFC1918.</strong>
    /// </summary>
    /// <remarks>
    /// Wenn diese Reihe rot wird, weil die Namen plötzlich <em>erlaubt</em>
    /// sind, ist das keine gute Nachricht: dann trifft die Politik auf einmal
    /// Namen, die sie nicht auflösen kann, und erlaubt damit mehr als
    /// beabsichtigt.
    /// </remarks>
    [Theory]
    [InlineData("http://consent-service:8002/consent/check")]
    [InlineData("http://identity-service:8001/auth/session")]
    [InlineData("http://notification-service:8010/notifications/send")]
    public void Ein_Containername_faellt_nicht_unter_die_Vorgabe(string adresse)
    {
        Vorgabe().IsAllowed(new Uri(adresse)).Should().BeFalse(
            "sonst reichte die Vorgabe fuer Dienst-zu-Dienst-Verkehr, und der "
            + "Test unten waere ueberfluessig");
    }

    /// <summary>Namentlich erlaubt, und dann geht es.</summary>
    /// <remarks>
    /// Das ist der Weg, den WorkerTransfer nehmen muss: jeder Dienst nennt die
    /// Hosts, die er wirklich ruft. Die Liste darf dabei nicht von Hand
    /// gepflegt werden — sie steht schon in der Umgebung
    /// (<c>Consent__Adresse</c>, <c>Jobs__Adresse</c>, …), und eine zweite
    /// Liste daneben wäre die, die als Erste veraltet.
    /// </remarks>
    [Fact]
    public void Namentlich_erlaubt_geht_durch()
    {
        var politik = Gebaut(bauer => bauer
            .AllowLoopback()
            .AllowPrivateNetworks()
            .Allow("consent-service", "api.anthropic.com"));

        politik.IsAllowed(new Uri("http://consent-service:8002/consent/check"))
            .Should().BeTrue();
        politik.IsAllowed(new Uri("https://api.anthropic.com/v1/messages"))
            .Should().BeTrue();

        // Und was nicht genannt ist, bleibt draussen — das ist der ganze Zweck.
        politik.IsAllowed(new Uri("https://telemetrie.beispiel.test/v1/traces"))
            .Should().BeFalse();
    }

    /// <summary>
    /// <strong>Die zwei Ziele, die wirklich nach draußen gehen.</strong>
    /// </summary>
    /// <remarks>
    /// GitHub und der Entwurfsanbieter sind die einzigen Hosts außerhalb des
    /// eigenen Netzes, die dieses System ruft. Beide müssen genannt werden,
    /// sonst stirbt der Nachweis einer GitHub-Verbindung und das Anschreiben
    /// mit „Anbieter antwortet nicht" — einer Meldung, die nach einem Ausfall
    /// bei GitHub aussieht und keiner ist.
    /// </remarks>
    [Theory]
    [InlineData("https://api.github.com/users/anna/repos")]
    [InlineData("https://api.anthropic.com/v1/messages")]
    public void Die_zwei_Aussenziele_brauchen_eine_Nennung(string adresse)
    {
        Vorgabe().IsAllowed(new Uri(adresse)).Should().BeFalse();
    }

    /// <summary>Eine IP aus dem Privatbereich fällt sehr wohl unter die Vorgabe.</summary>
    /// <remarks>
    /// Die Gegenprobe zur ersten Reihe: die Vorgabe ist nicht kaputt, sie
    /// versteht nur Namen nicht. Ohne diese Hälfte könnte der obige Befund auch
    /// heißen „die Politik erlaubt gar nichts".
    /// </remarks>
    [Theory]
    [InlineData("http://127.0.0.1:8002/consent/check")]
    [InlineData("http://10.0.0.5:8002/consent/check")]
    [InlineData("http://192.168.1.20:5432/")]
    public void Eine_private_Adresse_faellt_darunter(string adresse)
    {
        Vorgabe().IsAllowed(new Uri(adresse)).Should().BeTrue();
    }
}
