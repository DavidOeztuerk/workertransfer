using FluentAssertions;
using WorkerTransfer.ServiceDefaults.Ai;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// Known values leave as tokens and come back as themselves (VISION-FRAGEN E2).
/// </summary>
public sealed class PlaceholderTests
{
    [Fact]
    public void A_name_and_an_employer_do_not_survive_the_mask()
    {
        var placeholders = new Placeholders();
        placeholders.AddPersonName("Anna Muster");
        placeholders.Add("EMPLOYER", "Siemens", numbered: true);

        var masked = placeholders.Mask(
            "Ich bin Anna Muster. Bei siemens habe ich gelernt; Anna mag Schweißen.");

        masked.Should().NotContainAny("Anna", "Muster", "Siemens", "siemens");
        masked.Should().Be(
            "Ich bin [PERSON_NAME]. Bei [EMPLOYER_1] habe ich gelernt; "
            + "[PERSON_NAME_PART_1] mag Schweißen.");
    }

    [Fact]
    public void The_answer_gets_the_real_values_back()
    {
        var placeholders = new Placeholders();
        placeholders.AddPersonName("Anna Muster");
        placeholders.Add("EMPLOYER", "Siemens", numbered: true);

        placeholders.Restore("Bei [EMPLOYER_1] … Mit freundlichen Grüßen\n[ person_name ]")
            .Should().Be("Bei Siemens … Mit freundlichen Grüßen\nAnna Muster");
    }

    [Fact]
    public void Only_whole_words_are_masked()
    {
        var placeholders = new Placeholders();
        placeholders.Add("EMPLOYER", "Li", numbered: true);

        placeholders.Mask("Lieferant Li und Kali").Should().Be("Lieferant [EMPLOYER_1] und Kali");
    }

    [Fact]
    public void The_same_value_gets_the_same_token()
    {
        var placeholders = new Placeholders();

        placeholders.Add("EMPLOYER", "Bosch", numbered: true).Should().Be("[EMPLOYER_1]");
        placeholders.Add("EMPLOYER", "Siemens", numbered: true).Should().Be("[EMPLOYER_2]");
        placeholders.Add("EMPLOYER", "bosch", numbered: true).Should().Be("[EMPLOYER_1]");
    }

    [Fact]
    public void An_empty_value_registers_nothing()
    {
        var placeholders = new Placeholders();

        placeholders.Add("EMPLOYER", "  ", numbered: true).Should().Be("  ");
        placeholders.Tokens.Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_token_stays_as_it_is()
    {
        new Placeholders().Restore("Siehe [ANHANG]").Should().Be("Siehe [ANHANG]");
    }

    [Fact]
    public void A_fragment_hides_an_unfinished_token()
    {
        var placeholders = new Placeholders();
        placeholders.Add("EMPLOYER", "Siemens", numbered: true);

        placeholders.RestoreFragment("Bei [EMPLOYER_1] und [EMPLO").Should().Be("Bei Siemens und ");
    }
}
