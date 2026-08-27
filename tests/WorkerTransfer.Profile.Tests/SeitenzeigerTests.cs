using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Der Zeiger trägt beide Sortierschlüssel — und verzeiht Unfug.</summary>
public class SeitenzeigerTests
{
    [Fact]
    public void Hin_und_zurueck_ergibt_dasselbe()
    {
        var zeiger = new Seitenzeiger(
            new DateTimeOffset(2026, 8, 22, 9, 0, 0, 123, TimeSpan.Zero), SubjectId.New());

        Seitenzeiger.Lies(zeiger.Schreibe()).Should().Be(zeiger);
    }

    /// <summary>
    /// Zwei Profile in derselben Sekunde würden sich beim Blättern gegenseitig
    /// überspringen, trüge der Zeiger nur den Zeitstempel.
    /// </summary>
    [Fact]
    public void Zwei_Zeiger_zur_selben_Zeit_unterscheiden_sich_durch_die_Person()
    {
        var stempel = DateTimeOffset.UtcNow;

        new Seitenzeiger(stempel, SubjectId.New()).Schreibe()
            .Should().NotBe(new Seitenzeiger(stempel, SubjectId.New()).Schreibe());
    }

    /// <summary>
    /// Er kommt aus einer URL und wird kopiert, gekürzt und weitergereicht. Ein
    /// 400 darauf macht einen geteilten Link zu einer Fehlermeldung.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("kein-base64!")]
    [InlineData("bm9jaHdhcw==")]
    public void Unlesbares_ist_der_Anfang_und_kein_Fehler(string? unfug) =>
        Seitenzeiger.Lies(unfug).Should().BeNull();
}
