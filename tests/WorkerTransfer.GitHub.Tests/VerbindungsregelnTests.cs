using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Regeln, die am Aggregat hängen und nicht an einer Route.</summary>
/// <remarks>
/// Diese Reihe gibt es, weil eine Gegenprobe eine Lücke gezeigt hat: die
/// Prüfung „nur eine nachgewiesene Verbindung darf einen Abzug ablegen" ließ
/// sich über HTTP nicht widerlegen. Der Handler erreicht <c>Lege_ab</c> heute
/// nur nach geglücktem Nachweis — sie ist also eine zweite Verteidigungslinie,
/// und genau die fällt lautlos, wenn jemand später einen Knopf „jetzt laden"
/// baut. Ohne sie könnte jemand einen fremden Benutzernamen eintragen und
/// dessen Arbeit als seine zeigen, ohne je Zugriff auf das Konto gehabt zu
/// haben.
/// </remarks>
public class VerbindungsregelnTests
{
    private static readonly DateTimeOffset Jetzt = DateTimeOffset.UnixEpoch.AddDays(20_000);

    private static Verbindung Offen() =>
        Verbindung.Oeffne(new SubjectId(Guid.CreateVersion7()), "anna-dev");

    private static Repository Beleg(string name) =>
        new(name, "Ein Repository", "Go", 0, $"https://github.com/anna/{name}", Jetzt,
            ["Go"], ["cli"]);

    /// <summary>Ohne Nachweis kein Abzug.</summary>
    [Fact]
    public void Eine_unbewiesene_Verbindung_nimmt_keinen_Abzug_an()
    {
        var verbindung = Offen();

        var versuch = () => verbindung.Lege_ab(
            new Abzug([Beleg("fremde-arbeit")], true), Jetzt);

        versuch.Should().Throw<NichtNachgewiesen>();
        verbindung.Repositories.Should().BeEmpty();
        verbindung.GeholtAm.Should().BeNull();
    }

    /// <summary>Ein zweiter Nachweis ist keiner.</summary>
    [Fact]
    public void Zweimal_nachweisen_geht_nicht()
    {
        var verbindung = Offen();
        verbindung.Weise_nach(Jetzt);

        var versuch = () => verbindung.Weise_nach(Jetzt.AddDays(1));

        versuch.Should().Throw<SchonNachgewiesen>();
        verbindung.NachgewiesenAm.Should().Be(Jetzt);
    }

    /// <summary>
    /// Der Login hat keinen <em>öffentlichen</em> Setzer — er ändert sich nur
    /// über <see cref="Verbindung.Nenne_neu"/>, und das setzt den Nachweis
    /// zurück.
    /// </summary>
    /// <remarks>
    /// Ein öffentlicher Setzer wäre der Weg, den Namen nach dem Nachweis auf
    /// ein fremdes Konto zu drehen.
    /// <para>
    /// Geprüft wird die <em>öffentliche</em> Setzmethode, nicht
    /// <c>CanWrite</c>: das meldet auch einen privaten Setzer als schreibbar,
    /// und der ist hier richtig — <c>Nenne_neu</c> braucht ihn. Erst gemessen,
    /// dann geschrieben; der erste Anlauf prüfte <c>CanWrite</c> und war
    /// deshalb rot.
    /// </para>
    /// </remarks>
    [Fact]
    public void Der_Login_hat_keinen_oeffentlichen_Setzer()
    {
        typeof(Verbindung).GetProperty(nameof(Verbindung.Login))!
            .GetSetMethod(nonPublic: false).Should().BeNull();
    }

    /// <summary>
    /// Die Einmalzeichenfolge ist je Verbindung verschieden.
    /// </summary>
    /// <remarks>
    /// Wäre sie es nicht, bewiese ein einziger öffentlicher Gist jedes Konto.
    /// </remarks>
    [Fact]
    public void Jede_Verbindung_bekommt_eine_eigene_Zeichenfolge()
    {
        var zeichenfolgen = Enumerable.Range(0, 50)
            .Select(_ => Offen().Einmalzeichenfolge)
            .ToList();

        zeichenfolgen.Distinct().Should().HaveCount(50);
        zeichenfolgen.Should().OnlyContain(
            wert => wert.Length >= 20 && !wert.Contains('/') && !wert.Contains('+'));
    }

    /// <summary>Die Beschreibung trägt die Zeichenfolge und ein festes Präfix.</summary>
    [Fact]
    public void Die_Gistbeschreibung_traegt_die_Zeichenfolge()
    {
        Verbindung.Gistbeschreibung("abc123").Should().Be("workertransfer-verify-abc123");
    }
}
