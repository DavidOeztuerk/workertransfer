using System.Reflection;
using FluentAssertions;

using WorkerTransfer.Skills;

namespace WorkerTransfer.Skills.Tests;

/// <summary>The vocabulary renames and never infers anything.</summary>
public class SkillVocabularyTests
{
    [Theory]
    [InlineData("postgres", "PostgreSQL")]
    [InlineData("Postgres", "PostgreSQL")]
    [InlineData("POSTGRES", "PostgreSQL")]
    [InlineData("  psql  ", "PostgreSQL")]
    [InlineData("k8s", "Kubernetes")]
    [InlineData("dotnet", ".NET")]
    [InlineData("kundenservice", "Kundenbetreuung")]
    public void The_same_thing_gets_the_same_word(string getippt, string erwartet) =>
        Vocabulary.Kanonisch(getippt).Should().Be(erwartet);

    /// <summary>
    /// The core of ADR-0023: "React" means React and nothing else.
    /// </summary>
    /// <remarks>
    /// An inference would be a statement about a human being at a place where
    /// they cannot object. Whoever can do React and wants to name JavaScript
    /// names it.
    /// </remarks>
    [Fact]
    public void From_React_no_JavaScript_follows() =>
        Vocabulary.KanonischAlle(["React"]).Should().Equal("React");

    /// <summary>
    /// Like <see cref="From_React_no_JavaScript_follows"/>, in the place where
    /// the rule is harder to keep: "MIG/MAG" is not "Schweißen".
    /// </summary>
    [Fact]
    public void From_MIG_MAG_no_welding_follows() =>
        Vocabulary.KanonischAlle(["MIG/MAG"]).Should().Equal("MIG/MAG");

    /// <summary>
    /// A forklift licence is a piece of paper, not a forklift. The vocabulary
    /// carries both words and does not connect them.
    /// </summary>
    [Fact]
    public void A_certificate_is_not_a_machine() =>
        Vocabulary.KanonischAlle(["Staplerschein", "Gabelstapler"])
            .Should().Equal("Staplerschein", "Gabelstapler");

    /// <summary>The trades are canonicalised like everything else.</summary>
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
    public void Trade_terms_get_the_same_word(string getippt, string erwartet) =>
        Vocabulary.Kanonisch(getippt).Should().Be(erwartet);

    [Fact]
    public void What_is_unknown_stays_as_it_was_typed() =>
        Vocabulary.Kanonisch("Hufbeschlag").Should().Be("Hufbeschlag");

    [Fact]
    public void What_is_empty_falls_out_of_the_list() =>
        Vocabulary.KanonischAlle(["Go", "   ", "", "aws"])
            .Should().Equal("Go", "Amazon Web Services");

    /// <summary>
    /// The vocabulary deliberately does <em>not</em> deduplicate a list.
    /// </summary>
    /// <remarks>
    /// That belongs in <c>SkillList</c> and there AFTER the renaming — the
    /// other way round "Postgres, PostgreSQL" would stand twice.
    /// </remarks>
    [Fact]
    public void The_vocabulary_does_not_deduplicate() =>
        Vocabulary.KanonischAlle(["Postgres", "PostgreSQL"])
            .Should().Equal("PostgreSQL", "PostgreSQL");

    [Fact]
    public void A_name_never_stands_as_its_own_spelling()
    {
        foreach (var (name, schreibweisen) in Vocabulary.Schreibweisen)
        {
            schreibweisen.Should().NotContain(
                schreibweise => string.Equals(schreibweise, name, StringComparison.OrdinalIgnoreCase),
                $"'{name}' wird ohnehin erkannt");
        }
    }

    [Fact]
    public void A_spelling_belongs_to_at_most_one_name()
    {
        var alle = Vocabulary.Schreibweisen
            .SelectMany(entry => entry.Value)
            .Select(schreibweise => schreibweise.ToLowerInvariant())
            .ToList();

        alle.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// No name is at the same time a spelling of another (ADR-0023) —
    /// otherwise the result would hang on the order of the lookup.
    /// </summary>
    [Fact]
    public void No_name_is_the_spelling_of_another()
    {
        var namen = Vocabulary.Schreibweisen.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, schreibweisen) in Vocabulary.Schreibweisen)
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
    /// The boundary that is easiest to shift: at some point somebody wants a
    /// level, a weight or a ranking — and from then on the vocabulary evaluates
    /// human beings (ADR-0022, ADR-0023).
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
    public void The_vocabulary_knows_no_word_for_evaluation(string verboten)
    {
        var namen = typeof(Vocabulary)
            .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(member => member.Name);

        namen.Should().NotContain(
            name => name.Contains(verboten, StringComparison.OrdinalIgnoreCase));
    }
}
