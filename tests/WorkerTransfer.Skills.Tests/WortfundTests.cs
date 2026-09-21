using FluentAssertions;
using WorkerTransfer.Skills;

namespace WorkerTransfer.Skills.Tests;

/// <summary>
/// The term finder searches for what the vocabulary knows — and never guesses.
/// </summary>
/// <remarks>
/// <para>The suite pins both halves, and the second is the more important one:
/// a finder that also finds something where nothing stands is worse than none.
/// It would offer a human being a word about themselves that never stood in
/// their reference — and nobody looks the reference up.</para>
///
/// <para>Beside every promise therefore stands its counter-probe: "MIG/MAG" is
/// found, "Schweissen" is not inferred from it; "WIG" is found, the English
/// "wig" is not.</para>
/// </remarks>
public class WortfundTests
{
    /// <summary>The spelling out of the reference, the canonical name back.</summary>
    /// <remarks>
    /// The case this function exists for: in the reference the spelling without
    /// an eszett stands, in the profile the name should stand that a job names
    /// too. That is precisely a statement about <em>language</em> and permitted
    /// (ADR-0023).
    /// </remarks>
    [Theory]
    [InlineData("Herr Meier ist geprüfter Schweissfachmann.", "Schweißfachmann")]
    [InlineData("Herr Meier ist geprüfter Schweißfachmann (DVS).", "Schweißfachmann")]
    [InlineData("Beherrscht MIG/MAG-Schweißen sicher.", "MIG/MAG")]
    [InlineData("Kenntnisse in mig-mag.", "MIG/MAG")]
    [InlineData("Ausbildung zur Pflegefachfrau abgeschlossen.", "Pflegefachkraft")]
    [InlineData("Der Gabelstaplerschein liegt vor.", "Staplerschein")]
    [InlineData("Arbeitete mit postgres und node.", "PostgreSQL")]
    public void A_known_spelling_becomes_its_name(string text, string name) =>
        TermFinder.Find(text).Should().Contain(name);

    /// <summary>
    /// <strong>From "MIG/MAG" no "Schweißen" follows.</strong>
    /// </summary>
    /// <remarks>
    /// The counter-probe to the temptation, not the confirmation of the
    /// function — the same pair as <c>From_React_no_JavaScript_follows</c> in
    /// the vocabulary, only in the place where it is harder to keep: whoever
    /// searches a text has the related words literally in front of them.
    /// </remarks>
    [Fact]
    public void From_one_term_no_second_word_follows() =>
        TermFinder.Find("Beherrscht MIG/MAG.").Should().Equal("MIG/MAG");

    /// <summary>A word inside another word is not a term.</summary>
    /// <remarks>
    /// Without a word boundary "SPS" would find itself in every text with that
    /// sequence of letters and "go" in "gogo". The hyphen, by contrast, IS a
    /// boundary: in "SPS-Programmierung" SPS stands.
    /// </remarks>
    [Theory]
    [InlineData("Die Abkürzung stand mitten in ASPSPS drin.")]
    [InlineData("Er hat rechtzeitig und sorgfältig gearbeitet.")]
    public void The_inside_of_a_word_is_not_a_term(string text) =>
        TermFinder.Find(text).Should().BeEmpty();

    /// <summary>A hyphen separates, it does not hide.</summary>
    [Fact]
    public void A_hyphen_is_a_word_boundary() =>
        TermFinder.Find("Erfahrung in der SPS-Programmierung.").Should().Equal("SPS");

    /// <summary>
    /// <strong>Short abbreviations only in capitals.</strong>
    /// </summary>
    /// <remarks>
    /// "wig" is a hairpiece in English, "go" an everyday verb. A wrong
    /// suggestion is more expensive here than a missing one: it offers a human
    /// being a word about themselves that never stood in their document.
    /// </remarks>
    [Theory]
    [InlineData("She was wearing a wig at the ceremony.")]
    [InlineData("Please go to the office and ask.")]
    public void A_short_abbreviation_in_lower_case_is_not_a_term(string text) =>
        TermFinder.Find(text).Should().BeEmpty();

    /// <summary>The other half — otherwise the finding above would mean "nothing hits at all".</summary>
    [Theory]
    [InlineData("Schweissverfahren WIG und Autogen.", "WIG")]
    [InlineData("Programmierung von CNC-Maschinen.", "CNC")]
    public void The_same_abbreviation_in_capitals_is_a_term(string text, string name) =>
        TermFinder.Find(text).Should().Contain(name);

    /// <summary>A character in the name lifts the capitals rule.</summary>
    /// <remarks>
    /// "c#" and "k8s" are unmistakable in lower case too — the rule above
    /// protects against letter sequences that happen to be words as well, and
    /// these are not.
    /// </remarks>
    [Theory]
    [InlineData("Programmiert in c# seit 2019.", "C#")]
    [InlineData("Betrieb auf k8s.", "Kubernetes")]
    public void An_abbreviation_with_a_special_character_counts_in_any_casing(string text, string name) =>
        TermFinder.Find(text).Should().Contain(name);

    /// <summary>The longer name wins against the shorter piece inside it.</summary>
    /// <remarks>
    /// Without this order "MySQL" would not find itself inside "Microsoft SQL
    /// Server" — but the shorter window would win, and the longer name would
    /// never get its turn. The search therefore runs from the longest window
    /// downwards.
    /// </remarks>
    [Fact]
    public void The_longer_name_wins() =>
        TermFinder.Find("Betrieb von Microsoft SQL Server.").Should().Equal("Microsoft SQL Server");

    /// <summary>A line break in the middle of a name does not split it.</summary>
    /// <remarks>
    /// In a typeset document a break eventually stands between every pair of
    /// words. "Erste Hilfe" would otherwise not find itself.
    /// </remarks>
    [Fact]
    public void A_line_break_inside_a_name_does_not_split_it() =>
        TermFinder.Find("Teilnahme am Kurs\n  Erste\nHilfe   im Betrieb.").Should().Equal("Erste Hilfe");

    /// <summary>Every name once, in the order of its first occurrence.</summary>
    /// <remarks>
    /// An order over <em>words</em> in <em>one</em> document. Nowhere does a
    /// number come out of it, and no ranking over anybody follows from it
    /// (ADR-0022).
    /// </remarks>
    [Fact]
    public void Every_name_once_in_the_order_of_the_text() =>
        TermFinder.Find("CNC, dann SPS, dann nochmal CNC und zuletzt WIG.")
            .Should().Equal("CNC", "SPS", "WIG");

    /// <summary>What the vocabulary does not know is not found.</summary>
    /// <remarks>
    /// <strong>And that is the boundary, not the gap.</strong>
    /// "Bohrwerksdreher" is a real trade; inventing it here would mean
    /// concluding from the shape of a word that it is a skill. Whoever misses it
    /// extends the vocabulary by pull request — it is never learnt from people's
    /// documents.
    /// </remarks>
    [Fact]
    public void An_unknown_word_is_not_invented() =>
        TermFinder.Find("Er arbeitete als Bohrwerksdreher und Rohrleitungsbauer.")
            .Should().BeEmpty();

    /// <summary>No text, no term — and no error.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Without_text_there_is_nothing_to_find(string? text) =>
        TermFinder.Find(text).Should().BeEmpty();
}
