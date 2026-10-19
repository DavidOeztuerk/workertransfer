using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Transfer.Domain.Market;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>The earliest change from a stated notice period (VISION-FRAGEN H2).</summary>
/// <remarks>Notice given on Tuesday, 7 October 2026 in every case.</remarks>
public sealed class NoticePeriodTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static NoticePeriod Notice(int length, NoticeUnit unit, NoticeEnd end) =>
        NoticePeriod.State(new SubjectId(Guid.NewGuid()), length, unit, end, DateTimeOffset.UtcNow);

    [Theory]
    // § 622 (1) BGB: four weeks to the 15th or the end of a month.
    [InlineData(4, NoticeUnit.Week, NoticeEnd.FifteenthOrMonthEnd, "2026-11-15")]
    // Three months to the end of a month.
    [InlineData(3, NoticeUnit.Month, NoticeEnd.MonthEnd, "2027-01-31")]
    // Three months to the end of a quarter: 7 Jan 2027 → end of Q1.
    [InlineData(3, NoticeUnit.Month, NoticeEnd.QuarterEnd, "2027-03-31")]
    // Two weeks on any day.
    [InlineData(2, NoticeUnit.Week, NoticeEnd.AnyDay, "2026-10-21")]
    // One month to the 15th or month end, landing after the 15th.
    [InlineData(1, NoticeUnit.Month, NoticeEnd.FifteenthOrMonthEnd, "2026-11-15")]
    public void The_last_day_follows_the_rule(int length, NoticeUnit unit, NoticeEnd end, string expected) =>
        Notice(length, unit, end).LastDay(Today).Should().Be(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void The_earliest_start_is_the_day_after() =>
        Notice(3, NoticeUnit.Month, NoticeEnd.MonthEnd).EarliestStart(Today).Should().Be(new DateOnly(2027, 2, 1));

    [Theory]
    [InlineData(0, NoticeUnit.Month)]
    [InlineData(25, NoticeUnit.Month)]
    [InlineData(105, NoticeUnit.Week)]
    public void An_impossible_length_is_refused(int length, NoticeUnit unit)
    {
        var act = () => Notice(length, unit, NoticeEnd.AnyDay);

        act.Should().Throw<NoticeError>().WithMessage("*notice period*");
    }

    [Theory]
    [InlineData("2027-02-01", "2027-02-01")]
    [InlineData("2027-02", "2027-02-01")]
    [InlineData("sofort", null)]
    [InlineData("", null)]
    public void An_offers_start_is_read_or_left_alone(string start, string? expected) =>
        NoticeWords.ReadStart(start).Should().Be(
            expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
}
