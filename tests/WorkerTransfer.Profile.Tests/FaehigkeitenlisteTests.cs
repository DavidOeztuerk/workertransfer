using FluentAssertions;
using WorkerTransfer.Profile.Domain.Faehigkeiten;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Erst umbenennen, dann entdoppeln (ADR-0023).</summary>
public class FaehigkeitenlisteTests
{
    /// <summary>
    /// Die Reihenfolge der beiden Schritte ist die ganze Regel: entdoppelte man
    /// zuerst, blieben „Postgres“ und „PostgreSQL“ zwei Einträge — und der
    /// Abgleich im Browser zeigte demselben Menschen dieselbe Fähigkeit zweimal.
    /// </summary>
    [Fact]
    public void Zwei_Woerter_fuer_dieselbe_Sache_werden_ein_Eintrag() =>
        SkillList.Aus(["Postgres", "PostgreSQL", "psql"]).Werte
            .Should().Equal("PostgreSQL");

    [Fact]
    public void Die_erste_Schreibweise_gewinnt() =>
        SkillList.Aus(["Python", "python", "PYTHON"]).Werte.Should().Equal("Python");

    [Fact]
    public void Die_Reihenfolge_der_Person_bleibt() =>
        SkillList.Aus(["Kubernetes", "Go", "Altenpflege"]).Werte
            .Should().Equal("Kubernetes", "Go", "Altenpflege");

    [Fact]
    public void Nichts_gesagt_ist_eine_leere_Liste_und_kein_Fehler()
    {
        SkillList.Aus([]).Werte.Should().BeEmpty();
        SkillList.Aus(null).Werte.Should().BeEmpty();
    }

    [Fact]
    public void Eine_zu_lange_Faehigkeit_wird_abgelehnt()
    {
        var zuLang = new string('x', SkillList.Hoechstlaenge + 1);

        var action = () => SkillList.Aus([zuLang]);

        action.Should().Throw<SkillError>();
    }

    [Fact]
    public void Genau_die_Hoechstlaenge_geht_noch()
    {
        var gerade_noch = new string('x', SkillList.Hoechstlaenge);

        SkillList.Aus([gerade_noch]).Werte.Should().Equal(gerade_noch);
    }

    /// <summary>
    /// Gezählt wird nach dem Entdoppeln — sonst wiese die Grenze jemanden ab,
    /// dessen Liste am Ende eine einzige Fähigkeit ist.
    /// </summary>
    [Fact]
    public void Erst_entdoppeln_dann_zaehlen()
    {
        var vielMal = Enumerable.Repeat("Python", SkillList.Hoechstzahl + 1);

        SkillList.Aus(vielMal).Werte.Should().Equal("Python");
    }

    [Fact]
    public void Zu_viele_verschiedene_werden_abgelehnt()
    {
        var zuViele = Enumerable.Range(0, SkillList.Hoechstzahl + 1)
            .Select(nummer => $"Fach {nummer}");

        var action = () => SkillList.Aus(zuViele);

        action.Should().Throw<SkillError>();
    }
}
