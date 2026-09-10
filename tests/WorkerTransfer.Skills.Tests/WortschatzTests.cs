using System.Reflection;
using FluentAssertions;

using WorkerTransfer.Skills;

namespace WorkerTransfer.Skills.Tests;

/// <summary>Der Wortschatz benennt um und schließt nie etwas.</summary>
public class WortschatzTests
{
    [Theory]
    [InlineData("postgres", "PostgreSQL")]
    [InlineData("Postgres", "PostgreSQL")]
    [InlineData("POSTGRES", "PostgreSQL")]
    [InlineData("  psql  ", "PostgreSQL")]
    [InlineData("k8s", "Kubernetes")]
    [InlineData("dotnet", ".NET")]
    [InlineData("kundenservice", "Kundenbetreuung")]
    public void Dieselbe_Sache_bekommt_dasselbe_Wort(string getippt, string erwartet) =>
        Wortschatz.Kanonisch(getippt).Should().Be(erwartet);

    /// <summary>
    /// Der Kern von ADR-0023: „React“ heißt React und sonst nichts.
    /// </summary>
    /// <remarks>
    /// Eine Ableitung wäre eine Aussage über einen Menschen an einer Stelle, an
    /// der er nicht widersprechen kann. Wer React kann und JavaScript nennen
    /// will, nennt es.
    /// </remarks>
    [Fact]
    public void Aus_React_folgt_kein_JavaScript() =>
        Wortschatz.KanonischAlle(["React"]).Should().Equal("React");

    /// <summary>
    /// Dieselbe Prüfung wie <see cref="Aus_React_folgt_kein_JavaScript"/>, an
    /// dem Ort, an dem sie schwerer zu halten ist (ADR-0039).
    /// </summary>
    /// <remarks>
    /// Die Verlockung ist im Handwerk größer als in der IT, weil die
    /// Fachbegriffe eine sichtbare Ordnung haben: jeder weiß, dass MIG/MAG ein
    /// Schweißverfahren ist. Genau deshalb steht dieser Test hier. Wer MIG/MAG
    /// genannt hat, hat NICHT „Schweißen" genannt — und eine Suche nach
    /// „Schweißen" findet ihn nicht. Wer beides nennen will, nennt beides.
    /// </remarks>
    [Fact]
    public void Aus_MIG_MAG_folgt_kein_Schweissen() =>
        Wortschatz.KanonischAlle(["MIG/MAG"]).Should().Equal("MIG/MAG");

    /// <summary>
    /// Ein Staplerschein ist ein Papier, kein Gabelstapler — und umgekehrt.
    /// </summary>
    /// <remarks>
    /// Die zweite Hälfte derselben Grenze: der Wortschatz führt beide Wörter,
    /// und er verbindet sie nicht. Wer den Schein hat, hat damit nicht gesagt,
    /// dass er fährt, und wer fährt, hat nicht gesagt, dass er den Schein hat.
    /// Das zu verbinden wäre bequem und wäre eine Aussage über einen Menschen.
    /// </remarks>
    [Fact]
    public void Ein_Schein_ist_keine_Maschine() =>
        Wortschatz.KanonischAlle(["Staplerschein", "Gabelstapler"])
            .Should().Equal("Staplerschein", "Gabelstapler");

    /// <summary>Das Handwerk wird kanonisiert wie alles andere auch.</summary>
    [Theory]
    [InlineData("mig-mag", "MIG/MAG")]
    [InlineData("MIG MAG", "MIG/MAG")]
    [InlineData("tig", "WIG")]
    [InlineData("plc", "SPS")]
    [InlineData("gabelstaplerschein", "Staplerschein")]
    [InlineData("geruestbau", "Gerüstbau")]
    [InlineData("kfz-mechaniker", "Kfz-Mechatronik")]
    [InlineData("code 95", "Berufskraftfahrer-Qualifikation")]
    [InlineData("gesundheits- und krankenpfleger", "Pflegefachkraft")]
    [InlineData("  Ameise  ", "Hubwagen")]
    public void Handwerksbegriffe_bekommen_dasselbe_Wort(string getippt, string erwartet) =>
        Wortschatz.Kanonisch(getippt).Should().Be(erwartet);

    /// <summary>
    /// Ein Beruf außerhalb der IT bleibt stehen, wenn der Wortschatz ihn nicht
    /// kennt — er wird nicht abgelehnt und nicht ersetzt.
    /// </summary>
    [Fact]
    public void Unbekanntes_bleibt_stehen_wie_getippt() =>
        Wortschatz.Kanonisch("Hufbeschlag").Should().Be("Hufbeschlag");

    [Fact]
    public void Leeres_faellt_aus_der_Liste() =>
        Wortschatz.KanonischAlle(["Go", "   ", "", "aws"])
            .Should().Equal("Go", "Amazon Web Services");

    /// <summary>
    /// Eine Liste entdoppelt der Wortschatz absichtlich <em>nicht</em>.
    /// </summary>
    /// <remarks>
    /// Das gehört in <see cref="Faehigkeitenliste"/> und dort NACH dem
    /// Umbenennen — andersherum stünde „Postgres, PostgreSQL“ zweimal.
    /// </remarks>
    [Fact]
    public void Der_Wortschatz_entdoppelt_nicht() =>
        Wortschatz.KanonischAlle(["Postgres", "PostgreSQL"])
            .Should().Equal("PostgreSQL", "PostgreSQL");

    [Fact]
    public void Ein_Name_steht_nie_als_eigene_Schreibweise()
    {
        foreach (var (name, schreibweisen) in Wortschatz.Schreibweisen)
        {
            schreibweisen.Should().NotContain(
                schreibweise => string.Equals(schreibweise, name, StringComparison.OrdinalIgnoreCase),
                $"'{name}' wird ohnehin erkannt");
        }
    }

    [Fact]
    public void Eine_Schreibweise_gehoert_zu_hoechstens_einem_Namen()
    {
        var alle = Wortschatz.Schreibweisen
            .SelectMany(eintrag => eintrag.Value)
            .Select(schreibweise => schreibweise.ToLowerInvariant())
            .ToList();

        alle.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Kein Name ist zugleich Schreibweise eines anderen.
    /// </summary>
    /// <remarks>
    /// Die zweite Hälfte der Widerspruchsfreiheit aus ADR-0023 — bis dahin
    /// stand sie nur im Text. Sonst hinge das Ergebnis an der Reihenfolge des
    /// Nachschlagens: unsichtbar, und je nach Laufreihenfolge anders. Mit dem
    /// Handwerk wächst die Tabelle, und mit ihr die Gelegenheit.
    /// </remarks>
    [Fact]
    public void Kein_Name_ist_die_Schreibweise_eines_anderen()
    {
        var namen = Wortschatz.Schreibweisen.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, schreibweisen) in Wortschatz.Schreibweisen)
        {
            foreach (var schreibweise in schreibweisen)
            {
                namen.Should().NotContain(
                    schreibweise,
                    $"'{schreibweise}' ist Schreibweise von '{name}' und zugleich ein eigener Name");
            }
        }
    }

    /// <summary>
    /// Die Grenze, die sich am leichtesten verschieben lässt: irgendwann will
    /// jemand ein Niveau, ein Gewicht oder eine Rangfolge — und ab da bewertet
    /// der Wortschatz Menschen (ADR-0022, ADR-0023).
    /// </summary>
    [Theory]
    [InlineData("level")]
    [InlineData("niveau")]
    [InlineData("weight")]
    [InlineData("gewicht")]
    [InlineData("score")]
    [InlineData("punkt")]
    [InlineData("rank")]
    [InlineData("rang")]
    [InlineData("implies")]
    [InlineData("impliziert")]
    public void Der_Wortschatz_kennt_kein_Wort_fuer_Bewertung(string verboten)
    {
        var namen = typeof(Wortschatz)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(mitglied => mitglied.Name);

        namen.Should().NotContain(
            name => name.Contains(verboten, StringComparison.OrdinalIgnoreCase));
    }
}
