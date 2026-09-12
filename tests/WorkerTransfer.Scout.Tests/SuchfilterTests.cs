using FluentAssertions;
using WorkerTransfer.Scout.Domain.Suchen;

namespace WorkerTransfer.Scout.Tests;

/// <summary>Der Filter benennt um, folgert nie — und hat Grenzen.</summary>
public class SuchfilterTests
{
    /// <summary>„Postgres" und „PostgreSQL" sind dieselbe Suche.</summary>
    /// <remarks>
    /// Derselbe Wortschatz wie im Profil (ADR-0023). Zwei Schreibweisen wären
    /// zwei Suchen mit zwei Ergebnissen — und in der Häkchenliste stünde
    /// derselbe Haken zweimal.
    /// </remarks>
    [Fact]
    public void Zwei_Schreibweisen_werden_eine() =>
        Suchfilter.Aus(["postgres", "PostgreSQL"], null, false)
            .GenannteWorte.Should().ContainSingle();

    /// <summary>Erst umbenennen, dann entdoppeln — in dieser Reihenfolge.</summary>
    /// <remarks>
    /// Andersherum überstünden beide Schreibweisen das Entdoppeln und würden
    /// erst danach zum selben Wort: zwei Haken für eine Fähigkeit.
    /// </remarks>
    [Fact]
    public void Die_Reihenfolge_ist_umbenennen_dann_entdoppeln() =>
        Suchfilter.Aus(["postgres", "Postgres", "POSTGRESQL"], null, false)
            .GenannteWorte.Should().HaveCount(1);

    /// <summary>Ein unbekanntes Wort bleibt, wie es getippt wurde.</summary>
    /// <remarks>
    /// Der Wortschatz benennt um, er lehnt nicht ab: eine Liste erlaubter
    /// Fähigkeiten wäre eine Behauptung darüber, welche Arbeit es gibt
    /// (ADR-0023).
    /// </remarks>
    [Fact]
    public void Ein_unbekanntes_Wort_bleibt_stehen() =>
        Suchfilter.Aus(["Klöppelarbeit"], null, false)
            .GenannteWorte.Should().Equal("Klöppelarbeit");

    /// <summary>Leeres und Weissraum fallen weg, ohne einen Fehler zu erzeugen.</summary>
    [Fact]
    public void Leere_Worte_fallen_weg() =>
        Suchfilter.Aus(["Go", "   ", ""], null, false)
            .GenannteWorte.Should().Equal("Go");

    /// <summary>Elf Worte sind zu viele.</summary>
    /// <remarks>
    /// Gezählt wird <em>nach</em> dem Entdoppeln: sonst wiese eine Suche mit
    /// elfmal „Go" ab, obwohl daraus ein Wort wird.
    /// </remarks>
    [Fact]
    public void Zu_viele_Worte_werden_abgewiesen()
    {
        var elf = Enumerable.Range(0, 11).Select(nummer => $"wort{nummer}").ToArray();

        FluentActions.Invoking(() => Suchfilter.Aus(elf, null, false))
            .Should().Throw<Eingabefehler>();

        var elfmalDasselbe = Enumerable.Repeat("Go", 11).ToArray();

        FluentActions.Invoking(() => Suchfilter.Aus(elfmalDasselbe, null, false))
            .Should().NotThrow("nach dem Entdoppeln ist es ein Wort");
    }

    /// <summary>Ein zu langes Wort und ein zu langer Ort werden abgewiesen.</summary>
    [Fact]
    public void Zu_lange_Eingaben_werden_abgewiesen()
    {
        FluentActions.Invoking(() => Suchfilter.Aus(
                [new string('x', Suchfilter.HoechstlaengeWort + 1)], null, false))
            .Should().Throw<Eingabefehler>();

        FluentActions.Invoking(() => Suchfilter.Aus(
                null, new string('x', Suchfilter.HoechstlaengeOrt + 1), false))
            .Should().Throw<Eingabefehler>();
    }

    /// <summary>Die Meldung nennt die Regel, nie den Wert.</summary>
    /// <remarks>
    /// Girders <c>ValidationBehavior</c> protokolliert Fehlermeldungen. Ein
    /// Suchbegriff ist die Eingabe eines Menschen über einen anderen und hat im
    /// Protokoll nichts zu suchen.
    /// </remarks>
    [Fact]
    public void Die_Meldung_nennt_die_Regel_nie_den_Wert()
    {
        var zuLang = new string('x', Suchfilter.HoechstlaengeWort + 1);

        FluentActions.Invoking(() => Suchfilter.Aus([zuLang], null, false))
            .Should().Throw<Eingabefehler>()
            .Which.Message.Should().NotContain(zuLang);
    }

    /// <summary>Eine Suche ohne Wort ist erlaubt.</summary>
    /// <remarks>
    /// „Zeig mir, wer sich zeigen will" ist eine sinnvolle Frage — und sie zeigt
    /// weiterhin nur, wer freigegeben hat.
    /// </remarks>
    [Fact]
    public void Eine_Suche_ohne_Wort_ist_erlaubt() =>
        Suchfilter.Aus(null, null, false).GenannteWorte.Should().BeEmpty();

    /// <summary>Eine gespeicherte Zeile wird nicht nachträglich abgelehnt.</summary>
    /// <remarks>
    /// Sie war bei ihrer Entstehung gültig. Sie beim Lesen abzulehnen hiesse,
    /// jemandem seine gespeicherte Suche zu entziehen, weil später eine
    /// Obergrenze sank.
    /// </remarks>
    [Fact]
    public void Eine_gespeicherte_Zeile_wird_nicht_nachtraeglich_abgelehnt()
    {
        var zuViele = Enumerable.Range(0, 20).Select(nummer => $"wort{nummer}").ToArray();

        Suchfilter.Stelle_her(zuViele, "Berlin", true)
            .GenannteWorte.Should().HaveCount(20);
    }
}
