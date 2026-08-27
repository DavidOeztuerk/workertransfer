using FluentAssertions;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>Die Landkarte hängt nicht an ihrer Zeilenreihenfolge.</summary>
/// <remarks>
/// Gemessen, und der Grund, warum die <c>Priority</c>-Werte keine Verzierung
/// sind: <strong>ohne sie entscheidet die Dateireihenfolge.</strong> Alle
/// Ausnahmen auf denselben Wert zu setzen sah zunächst harmlos aus — die Reihe
/// blieb grün, weil die Zeilen zufällig richtig standen. Erst das Umdrehen hat
/// acht Routen umfallen lassen.
/// <para>
/// Damit ist das hier die Prüfung, die den Nächsten schützt, der eine Route
/// unten anhängt: dieselben Fragen an dieselbe Landkarte, nur rückwärts
/// geladen.
/// </para>
/// </remarks>
[Collection(UmgedrehteSammlung.Name)]
public class ReihenfolgeTests(UmgedreheteLandschaft landschaft)
{
    /// <summary>Die Ausnahmen — genau die, die ohne Priorität umfallen.</summary>
    [Theory]
    [InlineData("/jobs/7f000001-0000-0000-0000-000000000000/applications", "applications")]
    [InlineData("/companies/me/jobs", "jobs")]
    [InlineData("/companies/me/application-stats", "applications")]
    [InlineData("/companies/me/profile", "companies")]
    [InlineData("/companies/by-slug/muster-gmbh", "companies")]
    [InlineData("/companies/7f000001-0000-0000-0000-000000000000/profile", "companies")]
    [InlineData("/me/notification-preferences", "notification")]
    public async Task Die_Ausnahmen_gewinnen_auch_rueckwaerts(string pfad, string erwartet)
    {
        var (dienst, _) = await landschaft.Frage(pfad);

        dienst.Should().Be(erwartet);
    }

    /// <summary>Und die Regelfälle bleiben, wo sie sind.</summary>
    [Theory]
    [InlineData("/me", "identity")]
    [InlineData("/companies", "identity")]
    [InlineData("/jobs", "jobs")]
    [InlineData("/notifications", "notification")]
    public async Task Die_Regelfaelle_bleiben_auch_rueckwaerts(string pfad, string erwartet)
    {
        var (dienst, _) = await landschaft.Frage(pfad);

        dienst.Should().Be(erwartet);
    }

    /// <summary>Die Navigationsregel hängt gar nicht an der Landkarte.</summary>
    [Fact]
    public async Task Eine_Navigation_landet_auch_rueckwaerts_bei_der_Oberflaeche()
    {
        var (dienst, _) = await landschaft.Frage("/jobs", ("Sec-Fetch-Dest", "document"));

        dienst.Should().Be("web");
    }
}
