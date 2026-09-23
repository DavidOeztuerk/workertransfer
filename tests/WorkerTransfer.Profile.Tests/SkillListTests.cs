using FluentAssertions;
using WorkerTransfer.Profile.Domain.Skills;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Rename first, then deduplicate (ADR-0023).</summary>
public class SkillListTests
{
    /// <summary>
    /// The order of the two steps is the whole rule: deduplicating first would
    /// leave "Postgres" and "PostgreSQL" two entries — and the comparison in the
    /// browser would show the same human being the same skill twice.
    /// </summary>
    [Fact]
    public void Two_words_for_the_same_thing_become_one_entry() =>
        SkillList.From(["Postgres", "PostgreSQL", "psql"]).Values
            .Should().Equal("PostgreSQL");

    [Fact]
    public void The_first_spelling_wins() =>
        SkillList.From(["Python", "python", "PYTHON"]).Values.Should().Equal("Python");

    /// <remarks>
    /// "Altenpflege" (elderly care) stays German: it is what a person types on
    /// this platform, and it is not a developer's skill on purpose.
    /// </remarks>
    [Fact]
    public void The_persons_order_stays() =>
        SkillList.From(["Kubernetes", "Go", "Altenpflege"]).Values
            .Should().Equal("Kubernetes", "Go", "Altenpflege");

    [Fact]
    public void Saying_nothing_is_an_empty_list_and_no_error()
    {
        SkillList.From([]).Values.Should().BeEmpty();
        SkillList.From(null).Values.Should().BeEmpty();
    }

    [Fact]
    public void A_skill_that_is_too_long_is_refused()
    {
        var tooLong = new string('x', SkillList.MaxLength + 1);

        var action = () => SkillList.From([tooLong]);

        action.Should().Throw<SkillError>();
    }

    [Fact]
    public void Exactly_the_maximum_length_still_passes()
    {
        var justEnough = new string('x', SkillList.MaxLength);

        SkillList.From([justEnough]).Values.Should().Equal(justEnough);
    }

    /// <summary>
    /// Counted after deduplicating — otherwise the limit would turn away
    /// somebody whose list is a single skill in the end.
    /// </summary>
    [Fact]
    public void Deduplicate_first_then_count()
    {
        var manyTimes = Enumerable.Repeat("Python", SkillList.MaxEntries + 1);

        SkillList.From(manyTimes).Values.Should().Equal("Python");
    }

    [Fact]
    public void Too_many_different_ones_are_refused()
    {
        var tooMany = Enumerable.Range(0, SkillList.MaxEntries + 1)
            .Select(number => $"Subject {number}");

        var action = () => SkillList.From(tooMany);

        action.Should().Throw<SkillError>();
    }
}
