using FluentAssertions;
using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>The salary range keeps its rule — both ends or none (VISION-FRAGEN B5).</summary>
public sealed class SalaryBandTests
{
    [Fact]
    public void No_ends_is_no_range_and_no_error() =>
        SalaryBand.From(null, null, SalaryPeriod.Year).Should().BeNull();

    [Theory]
    [InlineData(40_000, null)]
    [InlineData(null, 60_000)]
    public void One_end_alone_is_refused(int? minimum, int? maximum)
    {
        // "From 40,000" reads like a range and hides its ceiling.
        var act = () => SalaryBand.From(minimum, maximum, SalaryPeriod.Year);

        act.Should().Throw<SalaryError>().WithMessage("*both ends*");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, SalaryBand.Ceiling + 1)]
    public void An_end_out_of_bounds_is_refused(int minimum, int maximum)
    {
        var act = () => SalaryBand.From(minimum, maximum, SalaryPeriod.Year);

        act.Should().Throw<SalaryError>();
    }

    [Fact]
    public void Upside_down_is_refused()
    {
        var act = () => SalaryBand.From(60_000, 40_000, SalaryPeriod.Year);

        act.Should().Throw<SalaryError>().WithMessage("*lower end is above*");
    }

    [Fact]
    public void A_single_figure_is_a_range_whose_ends_coincide() =>
        SalaryBand.From(18, 18, SalaryPeriod.Hour)!.Should()
            .BeEquivalentTo(new { Minimum = 18, Maximum = 18, Period = SalaryPeriod.Hour });
}
