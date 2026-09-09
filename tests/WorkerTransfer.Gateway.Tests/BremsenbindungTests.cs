using FluentAssertions;
using Girder.Infrastructure.Models;
using Microsoft.Extensions.Configuration;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>Bindet <c>ocelot.json</c> wirklich an Girders Bremsoptionen?</summary>
/// <remarks>
/// <para><strong>Der Grund für diese Reihe ist eine Messung, die
/// widersprach.</strong> Am laufenden Stapel gingen sechs Aufrufe auf
/// <c>/auth/register</c> alle durch, obwohl dort 5 je Minute steht, und die
/// Antwort trug <c>X-RateLimit-Limit: 200</c> — eine Zahl, die in
/// <c>ocelot.json</c> nirgends vorkommt.</para>
///
/// <para><c>BremsenkarteTests</c> konnte das nicht finden: sie prüft, dass jeder
/// gebremste Pfad eine Route hat — eine Aussage über die <em>Auswahl</em>, nicht
/// über die <em>Bindung</em>. Eine Konfiguration, die gar nicht ankommt, hat
/// weiterhin lauter gültige Pfade.</para>
///
/// <para>Diese Reihe schließt die Lücke an der Stelle, an der sie liegt: sie
/// liest dieselbe Datei, die der Dienst liest, bindet sie an denselben Typ und
/// fragt, was herauskommt.</para>
/// </remarks>
public sealed class BremsenbindungTests
{
    private static DistributedRateLimitingOptions Gebunden()
    {
        var pfad = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "gateway", "WorkerTransfer.Gateway", "ocelot.json");

        File.Exists(pfad).Should().BeTrue(
            "diese Reihe liest die ECHTE Landkarte — eine Kopie wuerde die "
            + "Frage nicht beantworten, sondern verdoppeln");

        var konfiguration = new ConfigurationBuilder()
            .AddJsonFile(Path.GetFullPath(pfad), optional: false)
            .Build();

        var optionen = new DistributedRateLimitingOptions();
        konfiguration
            .GetSection(DistributedRateLimitingOptions.SectionName)
            .Bind(optionen);

        return optionen;
    }

    /// <summary>
    /// <strong>Die drei Vorgaben stehen auf 0, und das muss ankommen.</strong>
    /// </summary>
    /// <remarks>
    /// Eine Grenze von 0 legt keinen Zähler an — nur deshalb zählen
    /// ausschließlich die fünf benannten Pfade. Bindet der Abschnitt nicht, so
    /// gelten Girders Vorgaben (100 je Minute), und dann zählt <em>jeder</em>
    /// Bildabruf der Oberfläche mit, die durch dasselbe Gateway läuft.
    /// </remarks>
    [Fact]
    public void Die_drei_Vorgaben_kommen_als_Null_an()
    {
        var optionen = Gebunden();

        optionen.RequestsPerMinute.Should().Be(0);
        optionen.RequestsPerHour.Should().Be(0);
        optionen.RequestsPerDay.Should().Be(0);
    }

    /// <summary>Je Herkunft, nie je Benutzer.</summary>
    /// <remarks>
    /// Girders Vorgabe ist <c>UserThenOrigin</c>. Käme die an, zählte ein
    /// angemeldeter Mensch gegen sich selbst statt gegen seine Herkunft — und
    /// die Bremse verlöre genau die Eigenschaft, wegen der sie am Eingang steht.
    /// </remarks>
    [Fact]
    public void Gezaehlt_wird_je_Herkunft()
    {
        Gebunden().Subject.Should().Be(RateLimitSubject.Origin);
    }

    /// <summary>Die fünf Pfade mit ihren Zahlen, wie sie dastehen.</summary>
    [Fact]
    public void Die_fuenf_Grenzen_kommen_an()
    {
        var grenzen = Gebunden().EndpointSpecificLimits;

        grenzen.Should().ContainKey("/auth/login");
        grenzen["/auth/login"].RequestsPerMinute.Should().Be(20);
        grenzen["/auth/register"].RequestsPerMinute.Should().Be(5);
        grenzen["/auth/resend-verification"].RequestsPerMinute.Should().Be(3);
        grenzen["/auth/verify-email"].RequestsPerMinute.Should().Be(20);
        grenzen["/auth/refresh"].RequestsPerMinute.Should().Be(60);
    }

    /// <summary>Und die Ausnahmelisten bleiben leer.</summary>
    /// <remarks>
    /// Der Binder <em>ergänzt</em> eine Sammlung und ersetzt sie nie. Stünde
    /// Loopback in Girders Vorgabe, käme man mit einer leeren Liste in der Datei
    /// nicht dagegen an — dann bremste lokal gar nichts, und man sähe es nicht.
    /// Seit 4.2.0 sind die Vorgaben leer; dieser Test hält fest, dass es so
    /// bleibt.
    /// </remarks>
    [Fact]
    public void Die_Ausnahmelisten_bleiben_leer()
    {
        var optionen = Gebunden();

        optionen.WhitelistedIps.Should().BeEmpty();
        optionen.WhitelistedEndpoints.Should().BeEmpty();
    }
}
