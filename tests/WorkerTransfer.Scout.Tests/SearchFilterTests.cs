using FluentAssertions;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Tests;

/// <summary>The filter renames, never infers — and has limits.</summary>
public class SearchFilterTests
{
    /// <summary>"Postgres" and "PostgreSQL" are the same search.</summary>
    /// <remarks>
    /// The same vocabulary as in the profile (ADR-0023). Two spellings would be
    /// two searches with two results — and the same tick would stand twice in the
    /// tick list.
    /// </remarks>
    [Fact]
    public void Two_spellings_become_one() =>
        SearchFilter.From(["postgres", "PostgreSQL"], null, false)
            .NamedWords.Should().ContainSingle();

    /// <summary>Rename first, then deduplicate — in this order.</summary>
    /// <remarks>
    /// The other way round both spellings would survive the deduplication and
    /// only become the same word afterwards: two ticks for one skill.
    /// </remarks>
    [Fact]
    public void The_order_is_rename_then_deduplicate() =>
        SearchFilter.From(["postgres", "Postgres", "POSTGRESQL"], null, false)
            .NamedWords.Should().HaveCount(1);

    /// <summary>An unknown word stays as it was typed.</summary>
    /// <remarks>
    /// The vocabulary renames, it does not refuse: a list of permitted skills
    /// would be a claim about which work exists (ADR-0023). The word is German
    /// with an umlaut on purpose: it proves the value passes byte for byte.
    /// </remarks>
    [Fact]
    public void An_unknown_word_stays() =>
        SearchFilter.From(["Klöppelarbeit"], null, false)
            .NamedWords.Should().Equal("Klöppelarbeit");

    /// <summary>Empty words and whitespace drop out without raising an error.</summary>
    [Fact]
    public void Empty_words_drop_out() =>
        SearchFilter.From(["Go", "   ", ""], null, false)
            .NamedWords.Should().Equal("Go");

    /// <summary>Eleven words are too many.</summary>
    /// <remarks>
    /// Counted <em>after</em> the deduplication: otherwise a search with "Go"
    /// eleven times would be refused, although it becomes one word.
    /// </remarks>
    [Fact]
    public void Too_many_words_are_refused()
    {
        var eleven = Enumerable.Range(0, 11).Select(number => $"word{number}").ToArray();

        FluentActions.Invoking(() => SearchFilter.From(eleven, null, false))
            .Should().Throw<InputError>();

        var elevenTimesTheSame = Enumerable.Repeat("Go", 11).ToArray();

        FluentActions.Invoking(() => SearchFilter.From(elevenTimesTheSame, null, false))
            .Should().NotThrow("after the deduplication it is one word");
    }

    /// <summary>A word that is too long and a location that is too long are refused.</summary>
    [Fact]
    public void Inputs_that_are_too_long_are_refused()
    {
        FluentActions.Invoking(() => SearchFilter.From(
                [new string('x', SearchFilter.MaxWordLength + 1)], null, false))
            .Should().Throw<InputError>();

        FluentActions.Invoking(() => SearchFilter.From(
                null, new string('x', SearchFilter.MaxLocationLength + 1), false))
            .Should().Throw<InputError>();
    }

    /// <summary>The message names the rule, never the value.</summary>
    /// <remarks>
    /// Noelia's <c>ValidationBehavior</c> logs error messages. A search term is
    /// one human being's input about another and has no business in the log.
    /// </remarks>
    [Fact]
    public void The_message_names_the_rule_never_the_value()
    {
        var tooLong = new string('x', SearchFilter.MaxWordLength + 1);

        FluentActions.Invoking(() => SearchFilter.From([tooLong], null, false))
            .Should().Throw<InputError>()
            .Which.Message.Should().NotContain(tooLong);
    }

    /// <summary>A search without a word is allowed.</summary>
    /// <remarks>
    /// "Show me who wants to be shown" is a sensible question — and it still
    /// shows only whoever has released.
    /// </remarks>
    [Fact]
    public void A_search_without_a_word_is_allowed() =>
        SearchFilter.From(null, null, false).NamedWords.Should().BeEmpty();

    /// <summary>A saved row is not refused after the fact.</summary>
    /// <remarks>
    /// It was valid when it was created. Refusing it on reading would mean taking
    /// somebody's saved search away because a cap was lowered later.
    /// </remarks>
    [Fact]
    public void A_saved_row_is_not_refused_after_the_fact()
    {
        var tooMany = Enumerable.Range(0, 20).Select(number => $"word{number}").ToArray();

        SearchFilter.Restore(tooMany, "Berlin", true)
            .NamedWords.Should().HaveCount(20);
    }
}
